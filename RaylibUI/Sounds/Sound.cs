using System.Collections.Concurrent;
using Civ2engine;

namespace RaylibUI
{
  /// <summary>
  /// Sound Management Class
  /// </summary>
  /// 
//ToDo: I think the play sound functions need to be reworked and made seperate from effects/music
//Maybe we store that cache data as json if we're to add more to it? 
  public class Sound : IDisposable
  {
    private readonly Thread _cacheSyncThread;
    private bool _soundDataCacheIsRunning;
    private DateTime _soundLastPlayed = DateTime.Now;
    private bool _soundDataCacheIsInvalid;
    private bool _soundDataCacheUpdating;
    public readonly ConcurrentBag<SoundData> SoundDataCache;

    private static string _convertedSoundsDir = String.Empty;
    private static readonly object SoundCacheLock = new();

    // Music player state. Driven every frame from the main loop (UpdateLoop) so music
    // survives screen transitions. Either loops _currentMusic, or plays a one-shot
    // queue of tracks (e.g. the MENUEND -> MENUOK game-start sequence) then stops.
    private SoundData? _currentMusic;
    private bool _loopCurrent;
    private readonly Queue<string> _musicQueue = new();

    public Sound(string activeInterface)
    {
      _convertedSoundsDir = Path.Combine(Settings.BasePath, "CONVERTEDSOUNDS-" + activeInterface.Replace(" ","").ToUpperInvariant());
      if (!Directory.Exists(_convertedSoundsDir))
        Directory.CreateDirectory(_convertedSoundsDir);
      SoundDataCache = ReloadAllExistingConvertedSoundReferences();
      _soundDataCacheIsRunning = true;
      _cacheSyncThread = new Thread(CacheSyncLoop);
      _cacheSyncThread.Start();
    }

    #region caching

    private void CacheSyncLoop()
    {
      while (_soundDataCacheIsRunning)
      {
        // Check if 10 seconds have passed since soundLastPlayed
        if (_soundDataCacheIsInvalid && !_soundDataCacheUpdating &&
            DateTime.Now - _soundLastPlayed >= TimeSpan.FromSeconds(10))
        {
          _soundDataCacheIsInvalid = false;
          _soundDataCacheUpdating = true;
          // Call SynchronizeCacheReference to update the cache
          SynchronizeCacheReference();
          _soundDataCacheUpdating = false;
        }

        // Sleep for 5 seconds
        Thread.Sleep(5000);
      }
    }

    private ConcurrentBag<SoundData> ReloadAllExistingConvertedSoundReferences()
    {
      string cachePath = Path.Combine(_convertedSoundsDir,"SoundCache.txt");
      var cache = new ConcurrentBag<SoundData>();
      if (File.Exists(cachePath))
      {
        lock (SoundCacheLock)
        {
          using (StreamReader reader = new StreamReader(cachePath))
          {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
              SoundData soundData = SoundData.FromCacheString(line, _convertedSoundsDir);
              if (soundData != null)
              {
                cache.Add(soundData);
              }
            }
          }
        }
      }

      return cache;
    }

    public void SynchronizeCacheReference()
    {
      string cachePath = $"{_convertedSoundsDir}SoundCache.txt";
      lock (SoundCacheLock)
      {
        using (StreamWriter writer = new StreamWriter(cachePath))
        {
          foreach (SoundData soundData in SoundDataCache)
          {
            string cacheString = soundData.ToCacheString();
            writer.WriteLine(cacheString);
          }
        }
      }
    }

    public void AddToCache(SoundData soundData)
    {
      SoundDataCache.Add(soundData);
      _soundDataCacheIsInvalid = true;
    }

    #endregion

    public SoundData? PlayCiv2DefaultSound(string soundName, bool loop = false)
    {
      var pth = Utils.GetFilePath(soundName, Settings.SearchPaths.Select(p=>Path.Combine(p,"Sound")), "wav");

      return pth != null ? PlaySound(pth, loop) : null;
    }

    public SoundData? PlaySound(string soundPath, bool loop = false)
    {
      if (!File.Exists(soundPath))
        return null;

      var x = SoundDataCache.FirstOrDefault(o => o.PathFull == soundPath);
      if (x == null)
      {
        x = new SoundData(soundPath, _convertedSoundsDir);
        if (!x.IsConverted)
        {
          x.Convert();
          AddToCache(x);
        }
      }

      _soundLastPlayed = DateTime.Now;

      if (x.IsConverted)
      {
        if (loop)
        {
          x.LoopSound();
          return x;
        }

        x.Play();
        return x;
      }

      return null;
    }

    /// <summary>
    /// Play a single track on repeat (e.g. the main menu theme).
    /// </summary>
    public void PlayMusicLoop(string soundName)
    {
      _musicQueue.Clear();
      _loopCurrent = true;
      StartTrack(soundName);
    }

    /// <summary>
    /// Play a sequence of tracks once each, then fall silent (e.g. the MENUEND ->
    /// MENUOK stinger when a game starts).
    /// </summary>
    public void PlayMusicSequence(params string[] soundNames)
    {
      _musicQueue.Clear();
      _loopCurrent = false;
      foreach (var name in soundNames) _musicQueue.Enqueue(name);
      StartTrack(_musicQueue.Count > 0 ? _musicQueue.Dequeue() : null);
    }

    private void StartTrack(string? soundName)
    {
      _currentMusic?.Stop();
      _currentMusic = string.IsNullOrEmpty(soundName) ? null : PlayCiv2DefaultSound(soundName);
    }

    /// <summary>
    /// Drives the music player: loops the current track or advances the queue when a
    /// track finishes. Call once per frame from the main loop so music survives screen
    /// transitions (menu -> game).
    /// </summary>
    public void UpdateLoop()
    {
      if (_currentMusic == null || _currentMusic.IsPlaying) return;

      if (_loopCurrent)
        _currentMusic.Play();
      else
        StartTrack(_musicQueue.Count > 0 ? _musicQueue.Dequeue() : null);
    }

    public void Dispose()
    {
      _soundDataCacheIsRunning = false;
      _cacheSyncThread.Join();
      SynchronizeCacheReference();

      foreach (var i in SoundDataCache)
      {
        i.Dispose();
      }
    }
  }
}
