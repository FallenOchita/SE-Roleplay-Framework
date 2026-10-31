using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SE.Roleplay.Framework.Core.Persistence
{
    /// <summary>JSON file-backed player store. Each account is stored in its own file.</summary>
    public sealed class JsonPlayerDataStore : IPlayerDataStore
    {
        private readonly string _directory;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public JsonPlayerDataStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A data directory is required.", nameof(directory));
            _directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(_directory);
        }

        public async Task<PlayerRecord?> LoadAsync(ulong platformId, CancellationToken cancellationToken = default)
        {
            ValidateAccount(platformId);
            cancellationToken.ThrowIfCancellationRequested();
            var path = GetPath(platformId);
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var record = JsonConvert.DeserializeObject<PlayerRecord>(json, _settings);
                    if (record == null) throw new InvalidDataException("Player profile JSON was empty or invalid: " + path);
                    return record;
                }
            }
            finally { _ioLock.Release(); }
        }

        public async Task SaveAsync(PlayerRecord record, CancellationToken cancellationToken = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            ValidateAccount(record.PlatformId);
            var path = GetPath(record.PlatformId);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var json = JsonConvert.SerializeObject(record, _settings);

            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_directory);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    await writer.WriteAsync(json).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                _ioLock.Release();
            }
        }

        public async Task<IReadOnlyCollection<ulong>> GetKnownPlayersAsync(CancellationToken cancellationToken = default)
        {
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!Directory.Exists(_directory)) return Array.Empty<ulong>();
                return Directory.GetFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    .Select(name => ulong.Parse(name!, NumberStyles.None, CultureInfo.InvariantCulture))
                    .ToArray();
            }
            finally { _ioLock.Release(); }
        }

        private string GetPath(ulong platformId) =>
            Path.Combine(_directory, platformId.ToString(CultureInfo.InvariantCulture) + ".json");

        private static void ValidateAccount(ulong platformId)
        {
            if (platformId == 0) throw new ArgumentOutOfRangeException(nameof(platformId));
        }
    }
}
