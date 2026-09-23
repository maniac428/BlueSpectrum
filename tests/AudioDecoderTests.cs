using System.Buffers.Binary;
using System.IO;
using BlueSpectrum.Audio;
using NAudio.Wave;

namespace BlueSpectrum.Tests;

public static class AudioDecoderTests
{
    public static object Run()
    {
        var checks = new List<string>();
        void Check(string name, Action test)
        {
            test();
            checks.Add(name);
        }

        Check("PCM16: distinct L/R and signed full scale", () =>
        {
            var decoder = new StereoSampleDecoder(new WaveFormat(48000, 16, 2));
            byte[] packet = [0x00, 0x40, 0x00, 0xC0, 0x00, 0x80, 0xFF, 0x7F];
            var samples = decoder.Decode(packet, false, out var signal);
            Near(samples[0], .5f); Near(samples[1], -.5f);
            Near(samples[2], -1f); Near(samples[3], 32767 / 32768f);
            Require(signal && decoder.SampleRate == 48000, "PCM16 metadata/signal");
        });
        Check("PCM24: signed little endian extrema", () =>
        {
            var decoder = new StereoSampleDecoder(new WaveFormat(44100, 24, 2));
            byte[] packet = [0, 0, 0x80, 0xFF, 0xFF, 0x7F, 0, 0, 0x40, 0, 0, 0xC0];
            var samples = decoder.Decode(packet, false, out _);
            Near(samples[0], -1f); Near(samples[1], 8388607 / 8388608f);
            Near(samples[2], .5f); Near(samples[3], -.5f);
        });
        Check("PCM32: distinct L/R and signed full scale", () =>
        {
            var decoder = new StereoSampleDecoder(new WaveFormat(96000, 32, 2));
            byte[] packet = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(packet, int.MinValue);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), 1073741824);
            var samples = decoder.Decode(packet, false, out _);
            Near(samples[0], -1f); Near(samples[1], .5f);
        });
        Check("Float32: unequal channels, finite values and actual sample rate", () =>
        {
            var decoder = new StereoSampleDecoder(WaveFormat.CreateIeeeFloatWaveFormat(192000, 2));
            var samples = decoder.Decode(Floats(.25f, -.75f, float.NaN, float.PositiveInfinity), false, out _);
            Near(samples[0], .25f); Near(samples[1], -.75f);
            Near(samples[2], 0f); Near(samples[3], 0f);
            Require(decoder.SampleRate == 192000, "Sample rate changed");
        });
        Check("Silent packet flags discard stale nonzero buffer contents", () =>
        {
            var decoder = new StereoSampleDecoder(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
            var packet = Floats(.5f, -.5f);
            decoder.Decode(packet, false, out _);
            var samples = decoder.Decode(packet, true, out var signal);
            Require(!signal && samples.All(sample => sample == 0f), "Silent packet reused stale audio");
        });
        Check("Mono is explicitly duplicated to both displays", () =>
        {
            var decoder = new StereoSampleDecoder(new WaveFormat(48000, 16, 1));
            var samples = decoder.Decode([0, 0x40, 0, 0xC0], false, out _);
            Near(samples[0], .5f); Near(samples[1], .5f);
            Near(samples[2], -.5f); Near(samples[3], -.5f);
            Require(decoder.ChannelNote.Contains("모노"), "Mono was not identified");
        });
        Check("Extensible PCM24 in PCM32 preserves level and PCM subtype", () =>
        {
            var format = new WaveFormatExtensible(48000, 32, 2, false, 24, 3);
            var decoder = new StereoSampleDecoder(format);
            byte[] packet = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(packet, 0x40000000);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), unchecked((int)0xC0000000));
            var samples = decoder.Decode(packet, false, out _);
            Near(samples[0], .5f); Near(samples[1], -.5f);
        });
        Check("5.1 channel mask: FL/FR only, no center or surround mixing", () =>
        {
            var format = new WaveFormatExtensible(48000, 32, 6, true, 32, 0x3F);
            var decoder = new StereoSampleDecoder(format);
            var samples = decoder.Decode(Floats(.1f, .2f, .9f, .8f, .7f, .6f), false, out _);
            Require(samples.Length == 2, "Multichannel extraction length");
            Near(samples[0], .1f); Near(samples[1], .2f);
            Require(decoder.ChannelNote.Contains("전면 좌우만"), "Multichannel policy was not identified");
        });
        Check("Missing FL/FR, inconsistent masks and unsupported subtypes are rejected", () =>
        {
            Throws<NotSupportedException>(() => new StereoSampleDecoder(new WaveFormatExtensible(48000, 32, 2, true, 32, 12)));
            Throws<NotSupportedException>(() => new StereoSampleDecoder(new WaveFormatExtensible(48000, 32, 6, true, 32, 3)));
            Throws<NotSupportedException>(() => new StereoSampleDecoder(new WaveFormatExtensible(48000, 32, 2, Guid.NewGuid(), 32, 3)));
        });
        Check("Malformed packet frame lengths are rejected", () =>
        {
            var decoder = new StereoSampleDecoder(new WaveFormat(48000, 24, 2));
            Throws<InvalidDataException>(() => decoder.Decode(new byte[5], false, out _));
        });
        return new { passed = checks.Count, checks };
    }

    private static byte[] Floats(params float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        for (var index = 0; index < samples.Length; index++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4), samples[index]);
        return bytes;
    }

    private static void Near(float actual, float expected)
    {
        if (Math.Abs(actual - expected) > .0000001f)
            throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
