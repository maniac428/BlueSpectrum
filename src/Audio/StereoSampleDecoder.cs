using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using NAudio.Wave;

namespace BlueSpectrum.Audio;

/// <summary>Decodes endpoint packets without mixing L/R or changing the endpoint format.</summary>
internal sealed class StereoSampleDecoder
{
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private readonly bool _isFloat;
    private readonly int _bytesPerSample;
    private readonly int _blockAlign;
    private readonly int _leftOffset;
    private readonly int _rightOffset;
    private float[] _stereo = [];

    public int SampleRate { get; }
    public string Description { get; }
    public string ChannelNote { get; }

    public StereoSampleDecoder(WaveFormat format)
    {
        SampleRate = format.SampleRate;
        var channels = format.Channels;
        var bits = format.BitsPerSample;
        var encoding = format.Encoding;
        var channelMask = 0;
        var validBits = bits;

        if (format is WaveFormatExtensible extensible)
        {
            encoding = extensible.SubFormat == FloatSubFormat ? WaveFormatEncoding.IeeeFloat
                : extensible.SubFormat == PcmSubFormat ? WaveFormatEncoding.Pcm
                : throw new NotSupportedException($"지원하지 않는 오디오 하위 형식: {extensible.SubFormat}");
            channelMask = extensible.ChannelMask;
            validBits = extensible.ValidBitsPerSample;
        }

        _isFloat = encoding == WaveFormatEncoding.IeeeFloat;
        if ((!_isFloat && encoding != WaveFormatEncoding.Pcm) ||
            (_isFloat && bits != 32) || (!_isFloat && bits is not (16 or 24 or 32)) ||
            validBits <= 0 || validBits > bits || (_isFloat && validBits != 32))
            throw new NotSupportedException($"지원하지 않는 오디오 형식: {encoding}, {bits}비트 ({validBits} 유효 비트)");
        if (SampleRate <= 0 || channels is < 1 or > 32)
            throw new NotSupportedException("오디오 샘플레이트 또는 채널 수가 올바르지 않습니다.");

        _bytesPerSample = bits / 8;
        _blockAlign = format.BlockAlign;
        if (_blockAlign < channels * _bytesPerSample)
            throw new NotSupportedException("오디오 프레임 크기가 채널 형식과 일치하지 않습니다.");

        if (channelMask != 0 && BitOperations.PopCount((uint)channelMask) != channels)
            throw new NotSupportedException("오디오 채널 배치와 채널 수가 일치하지 않습니다.");

        if (channels == 1)
        {
            _leftOffset = _rightOffset = 0;
            ChannelNote = "모노 출력 · 같은 신호를 좌우에 표시";
        }
        else
        {
            // WAVEFORMATEXTENSIBLE channel order follows increasing speaker-mask bits.
            // FL/FR are bits 0/1; reject layouts that do not contain a stereo front pair.
            if (channelMask != 0 && (channelMask & 3) != 3)
                throw new NotSupportedException("전면 좌·우 채널이 없는 출력 형식입니다. Windows에서 스테레오 출력을 선택하세요.");
            _leftOffset = 0;
            _rightOffset = _bytesPerSample;
            ChannelNote = channels == 2 ? "좌우 독립 스테레오"
                : channelMask == 0 ? $"{channels}채널 · 배치 미지정, 첫 두 채널 표시"
                : $"{channels}채널 · 전면 좌우만 표시 (센터·서라운드 제외)";
        }
        var bitText = validBits == bits ? $"{bits}bit" : $"{validBits}/{bits}bit";
        Description = $"{SampleRate:N0} Hz · {(_isFloat ? "Float" : "PCM")} {bitText} · {channels}ch";
    }

    /// <summary>The returned array is reused. Consume synchronously; do not retain it.</summary>
    public float[] Decode(ReadOnlySpan<byte> packet, bool silent, out bool hasSignal)
    {
        if (packet.Length % _blockAlign != 0)
            throw new InvalidDataException("오디오 패킷 크기가 프레임 경계와 일치하지 않습니다.");
        var frames = packet.Length / _blockAlign;
        var length = checked(frames * 2);
        if (_stereo.Length != length)
            _stereo = new float[length];
        hasSignal = false;
        if (silent)
        {
            Array.Clear(_stereo);
            return _stereo;
        }
        for (var frame = 0; frame < frames; frame++)
        {
            var sourceOffset = frame * _blockAlign;
            var left = ReadSample(packet.Slice(sourceOffset + _leftOffset, _bytesPerSample));
            var right = ReadSample(packet.Slice(sourceOffset + _rightOffset, _bytesPerSample));
            _stereo[frame * 2] = left;
            _stereo[frame * 2 + 1] = right;
            hasSignal |= MathF.Abs(left) > 0.000001f || MathF.Abs(right) > 0.000001f;
        }
        return _stereo;
    }

    private float ReadSample(ReadOnlySpan<byte> bytes)
    {
        if (_isFloat)
        {
            var sample = BinaryPrimitives.ReadSingleLittleEndian(bytes);
            return float.IsFinite(sample) ? sample : 0f;
        }
        // Extensible PCM valid bits are left-aligned in their container. Scaling by the
        // container width also correctly handles e.g. 24 valid bits in a 32-bit word.
        return _bytesPerSample switch
        {
            2 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
            3 => ((bytes[0] | (bytes[1] << 8) | (bytes[2] << 16)) << 8 >> 8) / 8388608f,
            4 => (float)(BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0),
            _ => throw new InvalidOperationException()
        };
    }
}
