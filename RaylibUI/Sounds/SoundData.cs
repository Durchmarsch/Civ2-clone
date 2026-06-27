using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;
using Raylib_CSharp.Audio;

namespace RaylibUI;

public class SoundData : IDisposable
{
    public DateTime? LastPlayed { get; set; }
    private Raylib_CSharp.Audio.Sound _rlSound;
    private bool _rlSoundLoaded;
    public Music RlMusic;
    private bool _rlMusicLoaded;

    public bool IsConverted { get; set; }

    public bool IsPlaying => _rlSoundLoaded && _rlSound.IsPlaying();

    public string PathFull { get; }

    public string PathConv { get; }

    public SoundData(string fullpath, string convertedPath)
    {
        PathFull = fullpath;
        PathConv = Path.Combine(convertedPath, $"{GetUniqueIdentifier(fullpath)}.wav");
        IsConverted = File.Exists(PathConv);
    }

    public void Convert()
    {
        IsConverted = ConvertAudioPcmU8ToPcmS16Le(PathFull, PathConv);
        if (!IsConverted)
        {
            // NAudio's WaveFormatConversionStream relies on the Windows-only ACM
            // (Msacm32.dll) and throws on macOS/Linux. raylib/miniaudio loads the
            // original 8-bit PCM WAV natively, so fall back to playing it directly.
            IsConverted = File.Exists(PathFull);
        }
    }

    public void Stop()
    {
        _loopAsSound = false;
        if (_rlSoundLoaded)
            _rlSound.Stop();
        else if (_rlMusicLoaded)
            RlMusic.StopStream();
    }

    public void Play()
    {
        if (!_rlSoundLoaded)
        {
            var path = File.Exists(PathConv) ? PathConv : PathFull;
            if (File.Exists(path))
            {
                _rlSound = Raylib_CSharp.Audio.Sound.Load(path);
                _rlSoundLoaded = _rlSound.FrameCount > 0;
            }
        }

        if (_rlSoundLoaded)
        {
            _rlSound.Play();
            LastPlayed = DateTime.Now;
        }
    }

    private bool _loopAsSound;

    public Music LoopSound()
    {
        // Streamed Music (PlayMusicStream) does not reach the audio output on some
        // macOS setups, while one-shot Sounds do. So loop the track as a one-shot
        // Sound that is re-triggered each time it finishes (see MusicUpdateCall).
        if (!_rlSoundLoaded)
        {
            var path = File.Exists(PathConv) ? PathConv : PathFull;
            if (File.Exists(path))
            {
                _rlSound = Raylib_CSharp.Audio.Sound.Load(path);
                _rlSoundLoaded = _rlSound.FrameCount > 0;
            }
        }

        _loopAsSound = true;
        if (_rlSoundLoaded)
        {
            _rlSound.Play();
        }

        return RlMusic;
    }

    /// <summary>
    /// Must be called within the main while loop to keep looping audio alive.
    /// </summary>
    public void MusicUpdateCall()
    {
        if (_loopAsSound)
        {
            if (_rlSoundLoaded && !_rlSound.IsPlaying())
            {
                _rlSound.Play();
            }
            return;
        }

        if (RlMusic.FrameCount > 0)
            RlMusic.UpdateStream();
    }


    public bool ConvertAudioPcmU8ToPcmS16Le(string inputFilePath, string outputFilePath)
    {
        try
        {
            using (var reader = new WaveFileReader(inputFilePath))
            {
                var format = new WaveFormat(22050, 16, 2); // Desired output format: 22.05 kHz, 16-bit, Stereo (for raylib)

                using (var conversionStream = new WaveFormatConversionStream(format, reader))
                {
                    WaveFileWriter.CreateWaveFile(outputFilePath, conversionStream);
                }
            }
        }
        catch (DllNotFoundException ex)
        {
            //TODO: find alternative sound methods for OSX
            return false;
        }

        return true;
    }

    private string GetUniqueIdentifier(string inputString)
    {
        using (var md5 = MD5.Create())
        {
            var inputBytes = Encoding.UTF8.GetBytes(inputString);
            var hashBytes = md5.ComputeHash(inputBytes);

            var stringBuilder = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                stringBuilder.Append(hashBytes[i].ToString("x2")); // Convert each byte to a hexadecimal string representation
            }

            return stringBuilder.ToString();
        }
    }

    public void Dispose()
    {
        if (_rlSoundLoaded || _rlSound.FrameCount > 0)
        {
            Stop();
            _rlSound.Unload();
        }
    }

    internal static SoundData? FromCacheString(string line, string soundsDir)
    {
        return string.IsNullOrEmpty(line) ? null : new SoundData(line, soundsDir);
    }


    internal string ToCacheString()
    {
        return PathFull;
    }
}