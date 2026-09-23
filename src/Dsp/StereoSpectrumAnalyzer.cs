using System;

namespace BlueSpectrum.Dsp;

/// <summary>
/// Allocation-free, seven-band stereo power analysis. The audio worker owns this
/// instance; publish/copy the two result arrays under the service's snapshot lock.
/// This type deliberately does not marshal audio or access the user interface.
/// </summary>
public sealed class StereoSpectrumAnalyzer
{
    public static readonly double[] Centers = [63, 160, 400, 1000, 2500, 6300, 16000];
    public const double FloorDb = -100;

    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly int _hopSize;
    private readonly float[] _leftRing;
    private readonly float[] _rightRing;
    private readonly double[] _window;
    private readonly double[] _real;
    private readonly double[] _imaginary;
    private readonly int[] _bitReversed;
    private readonly double[] _twiddleReal;
    private readonly double[] _twiddleImaginary;
    private readonly double[,] _binWeights;
    private readonly double _powerNormalization;
    private readonly double _windowPowerNormalization;
    private int _writePosition;
    private int _framesUntilAnalysis;

    /// <summary>Latest band powers in dB relative to a full-scale constant of 1.</summary>
    public double[] LeftDb { get; } = new double[7];
    public double[] RightDb { get; } = new double[7];
    /// <summary>
    /// Total captured signal power, including DC through Nyquist, from the same
    /// Hann window as the bands. A full-scale sine reads -3.0103 dBFS.
    /// </summary>
    public double LeftFullRangeDb { get; private set; } = FloorDb;
    public double RightFullRangeDb { get; private set; } = FloorDb;
    /// <summary>Number of completed FFT windows since construction or Reset.</summary>
    public int FramesAnalyzed { get; private set; }
    public double WindowMilliseconds => 1000.0 * _fftSize / _sampleRate;

    public StereoSpectrumAnalyzer(int sampleRate, int fftSize = 4096, int hopSize = 1024)
    {
        if (sampleRate < 1000 || sampleRate > 768000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (fftSize < 256 || fftSize > 65536 || (fftSize & (fftSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(fftSize), "FFT size must be a power of two from 256 to 65536.");
        if (hopSize < 1 || hopSize > fftSize)
            throw new ArgumentOutOfRangeException(nameof(hopSize));

        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _hopSize = hopSize;
        _leftRing = new float[fftSize];
        _rightRing = new float[fftSize];
        _window = new double[fftSize];
        _real = new double[fftSize];
        _imaginary = new double[fftSize];
        _bitReversed = new int[fftSize];
        _twiddleReal = new double[fftSize / 2];
        _twiddleImaginary = new double[fftSize / 2];
        _binWeights = new double[7, fftSize / 2 + 1];

        double windowPower = 0;
        int bitCount = 0;
        for (int n = fftSize; n > 1; n >>= 1) bitCount++;
        for (int i = 0; i < fftSize; i++)
        {
            // Periodic Hann window, suitable for a block interpreted by a DFT.
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / fftSize);
            _window[i] = w;
            windowPower += w * w;
            int value = i;
            int reversed = 0;
            for (int bit = 0; bit < bitCount; bit++)
            {
                reversed = (reversed << 1) | (value & 1);
                value >>= 1;
            }
            _bitReversed[i] = reversed;
        }
        for (int i = 0; i < fftSize / 2; i++)
        {
            double angle = -2 * Math.PI * i / fftSize;
            _twiddleReal[i] = Math.Cos(angle);
            _twiddleImaginary[i] = Math.Sin(angle);
        }

        // With an unnormalized DFT, Parseval gives sum(|X|²)/N. Dividing
        // additionally by sum(window²) estimates unwindowed mean-square power.
        // Positive-frequency bins are doubled except DC and Nyquist. Thus an
        // amplitude-1 sine contained in a band reads 10*log10(1/2) = -3.0103 dBFS.
        // This is an RMS-power convention, NOT the AES17 sine-referenced scale.
        // Display gain and visual smoothing belong to the UI, not this analyzer.
        _powerNormalization = 1.0 / (fftSize * windowPower);
        _windowPowerNormalization = 1.0 / windowPower;

        double[] edges = new double[8];
        edges[0] = 20;
        for (int band = 1; band < 7; band++)
            edges[band] = Math.Sqrt(Centers[band - 1] * Centers[band]);
        edges[7] = Math.Min(20000, sampleRate / 2.0);

        double binWidth = sampleRate / (double)fftSize;
        double nyquist = sampleRate / 2.0;
        for (int band = 0; band < 7; band++)
        {
            double low = Math.Min(edges[band], nyquist);
            double high = Math.Min(edges[band + 1], Math.Min(20000, nyquist));
            if (high <= low) continue;
            for (int bin = 0; bin <= fftSize / 2; bin++)
            {
                // Divide edge bins in proportion to their frequency-cell overlap.
                // Adjacent bands partition power without duplicating edge bins.
                double cellLow = Math.Max(0, (bin - 0.5) * binWidth);
                double cellHigh = Math.Min(nyquist, (bin + 0.5) * binWidth);
                double overlap = Math.Max(0, Math.Min(high, cellHigh) - Math.Max(low, cellLow));
                double oneSidedFactor = bin == 0 || bin == fftSize / 2 ? 1 : 2;
                _binWeights[band, bin] = oneSidedFactor * overlap / (cellHigh - cellLow);
            }
        }
        Reset();
    }

    /// <summary>
    /// Feed complete interleaved L,R float frames. Finite samples are preserved
    /// even above full scale; invalid samples become silence. Any chunk size is
    /// accepted, provided its float count is even. No allocation occurs per hop.
    /// </summary>
    public void AddFrames(ReadOnlySpan<float> interleavedStereo)
    {
        if ((interleavedStereo.Length & 1) != 0)
            throw new ArgumentException("Input must contain complete left/right frames.", nameof(interleavedStereo));

        for (int i = 0; i < interleavedStereo.Length; i += 2)
        {
            float left = interleavedStereo[i];
            float right = interleavedStereo[i + 1];
            _leftRing[_writePosition] = float.IsFinite(left) ? left : 0;
            _rightRing[_writePosition] = float.IsFinite(right) ? right : 0;
            _writePosition = (_writePosition + 1) & (_fftSize - 1);
            if (--_framesUntilAnalysis != 0) continue;
            LeftFullRangeDb = AnalyzeChannel(_leftRing, LeftDb);
            RightFullRangeDb = AnalyzeChannel(_rightRing, RightDb);
            FramesAnalyzed++;
            _framesUntilAnalysis = _hopSize;
        }
    }

    public void Reset()
    {
        Array.Clear(_leftRing);
        Array.Clear(_rightRing);
        Array.Fill(LeftDb, FloorDb);
        Array.Fill(RightDb, FloorDb);
        LeftFullRangeDb = RightFullRangeDb = FloorDb;
        _writePosition = 0;
        _framesUntilAnalysis = _fftSize;
        FramesAnalyzed = 0;
    }

    private double AnalyzeChannel(float[] ring, double[] output)
    {
        // _writePosition points to the oldest sample after a complete window.
        // Load in bit-reversed order so the in-place FFT needs no permutation pass.
        double fullRangePower = 0;
        for (int i = 0; i < _fftSize; i++)
        {
            int reversed = _bitReversed[i];
            double windowedSample = ring[(_writePosition + i) & (_fftSize - 1)] * _window[i];
            _real[reversed] = windowedSample;
            fullRangePower += windowedSample * windowedSample;
            _imaginary[reversed] = 0;
        }
        for (int width = 2; width <= _fftSize; width <<= 1)
        {
            int half = width >> 1;
            int twiddleStep = _fftSize / width;
            for (int block = 0; block < _fftSize; block += width)
            {
                for (int j = 0; j < half; j++)
                {
                    int even = block + j;
                    int odd = even + half;
                    int twiddle = j * twiddleStep;
                    double wr = _twiddleReal[twiddle];
                    double wi = _twiddleImaginary[twiddle];
                    double tr = wr * _real[odd] - wi * _imaginary[odd];
                    double ti = wr * _imaginary[odd] + wi * _real[odd];
                    _real[odd] = _real[even] - tr;
                    _imaginary[odd] = _imaginary[even] - ti;
                    _real[even] += tr;
                    _imaginary[even] += ti;
                }
            }
        }

        for (int band = 0; band < 7; band++)
        {
            double power = 0;
            for (int bin = 0; bin <= _fftSize / 2; bin++)
            {
                double weight = _binWeights[band, bin];
                if (weight == 0) continue;
                power += (_real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]) * weight;
            }
            power *= _powerNormalization;
            output[band] = power > 1e-10 ? 10 * Math.Log10(power) : FloorDb;
        }
        // Normalize the time-domain sum by Hann power. Unlike summing the seven
        // displayed bands, this includes the entire captured signal bandwidth.
        fullRangePower *= _windowPowerNormalization;
        return fullRangePower > 1e-10 ? 10 * Math.Log10(fullRangePower) : FloorDb;
    }
}
