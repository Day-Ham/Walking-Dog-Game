using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace WalkingDog.Racing
{
    public static class RaceRoomCodes
    {
        // 32-character alphabet omitting easily confused glyphs (0, O, 1, I, L).
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        // Generates a cryptographically random human-friendly room code using an unambiguous alphabet.
        public static string Create(int length = 5)
        {
            if (length < 3 || length > 12) throw new ArgumentOutOfRangeException(nameof(length));
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
        }

        // Strips spaces and dashes, converts to uppercase, and verifies length and character set.
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var cleaned = input.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
            return cleaned.Length == 5 && cleaned.All(c => Alphabet.Contains(c)) ? cleaned : "";
        }

        // Formats a normalized room code with a central hyphen for readability.
        public static string Format(string code)
        {
            var norm = Normalize(code);
            if (norm.Length >= 4) return norm.Insert(2, "-");
            return norm;
        }
    }

    public sealed class RaceEntrant
    {
        public string PlayerId { get; }
        public string DisplayName { get; }
        public DogStats Stats { get; }
        public string EquippedItemId { get; }
        public bool IsHost { get; }

        // Constructs a race entrant representing a joined player and their dog stats.
        public RaceEntrant(string playerId, string displayName, DogStats stats, string equippedItemId = "", bool isHost = false)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Entrant needs a player ID.", nameof(playerId));
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            stats.Validate();
            PlayerId = playerId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Racer" : displayName.Trim();
            Stats = stats.Copy();
            EquippedItemId = equippedItemId ?? "";
            IsHost = isHost;
        }

        // Serializes entrant properties to a Firestore dictionary payload.
        public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["id"] = PlayerId,
            ["name"] = DisplayName,
            ["speed"] = (long)Stats.speed,
            ["stamina"] = (long)Stats.stamina,
            ["acceleration"] = (long)Stats.acceleration,
            ["equippedItem"] = EquippedItemId,
            ["isHost"] = IsHost
        };

        // Deserializes and validates an entrant from a Firestore document dictionary.
        public static RaceEntrant Parse(IDictionary<string, object> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string id = data.TryGetValue("id", out var idVal) ? idVal?.ToString() : "";
            string name = data.TryGetValue("name", out var nameVal) ? nameVal?.ToString() : "Racer";
            string equipped = data.TryGetValue("equippedItem", out var eqVal) ? eqVal?.ToString() : "";
            bool host = data.TryGetValue("isHost", out var hostVal) && hostVal is bool b && b;

            int ReadStat(string key)
            {
                if (data.TryGetValue(key, out var val) && val is long num && num >= 1 && num <= 100) return (int)num;
                throw new InvalidOperationException("The race room contains invalid dog stats.");
            }

            var stats = new DogStats
            {
                speed = ReadStat("speed"),
                stamina = ReadStat("stamina"),
                acceleration = ReadStat("acceleration")
            };
            stats.Validate();

            return new RaceEntrant(id, name, stats, equipped, host);
        }
    }

    public sealed class RaceRoom
    {
        public const int MaxEntrants = 4;
        public const double DefaultLength = 200;

        public string RoomCode { get; }
        public string HostUid { get; }
        public string HostName { get; }
        public string Status { get; } // "waiting", "racing", "closed"
        public int Seed { get; }
        public double TrackLength { get; }
        public IReadOnlyList<RaceEntrant> Entrants { get; }

        public bool IsFull => Entrants.Count >= MaxEntrants;
        public bool IsWaiting => Status == "waiting";
        public bool IsRacing => Status == "racing";
        public bool IsClosed => Status == "closed";

        // Constructs and validates a multiplayer race lobby room with entrants and track settings.
        public RaceRoom(string roomCode, string hostUid, string hostName, string status, int seed,
            double trackLength, IEnumerable<RaceEntrant> entrants)
        {
            RoomCode = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(RoomCode)) throw new ArgumentException("A room needs a valid room code.", nameof(roomCode));
            HostUid = hostUid ?? "";
            HostName = hostName ?? "Host";
            Status = string.IsNullOrWhiteSpace(status) ? "waiting" : status;
            Seed = seed;
            TrackLength = trackLength;
            Entrants = entrants?.ToList().AsReadOnly() ?? new List<RaceEntrant>().AsReadOnly();
            if (string.IsNullOrWhiteSpace(HostUid) || (Status != "waiting" && Status != "racing" && Status != "closed")
                || TrackLength != DefaultLength || Entrants.Count < 1 || Entrants.Count > MaxEntrants
                || Seed < 0 || (Status == "waiting" && Seed != 0) || (Status == "racing" && Seed == 0)
                || Entrants.Select(e => e.PlayerId).Distinct(StringComparer.Ordinal).Count() != Entrants.Count
                || Entrants[0].PlayerId != HostUid || !Entrants[0].IsHost || Entrants.Skip(1).Any(e => e.IsHost))
                throw new InvalidOperationException("The race room could not be verified.");
        }

        // Checks whether the requesting user is the host and the room is ready to start.
        public bool CanStart(string requesterUid) =>
            IsWaiting && HostUid == requesterUid && Entrants.Count >= 1;

        // Converts room properties and entrant list to a Firestore dictionary payload.
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["code"] = RoomCode,
                ["hostUid"] = HostUid,
                ["hostName"] = HostName,
                ["status"] = Status,
                ["seed"] = (long)Seed,
                ["length"] = TrackLength,
                ["entrants"] = Entrants.Select(e => e.ToDictionary()).ToList()
            };
            return dict;
        }

        // Deserializes and validates a race room document from Firestore data.
        public static RaceRoom Parse(string code, IDictionary<string, object> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string hostUid = data.TryGetValue("hostUid", out var hVal) ? hVal?.ToString() : "";
            string hostName = data.TryGetValue("hostName", out var hnVal) ? hnVal?.ToString() : "Host";
            string status = data.TryGetValue("status", out var sVal) ? sVal?.ToString() : "waiting";
            int seed = data.TryGetValue("seed", out var seedVal) && seedVal is long sNum ? (int)sNum : 0;
            double length = data.TryGetValue("length", out var lenVal) && lenVal is double lNum ? lNum
                : (lenVal is long lInt ? (double)lInt : DefaultLength);

            var entrants = new List<RaceEntrant>();
            if (data.TryGetValue("entrants", out var rawList) && rawList is IEnumerable<object> list)
            {
                foreach (var item in list)
                {
                    if (item is IDictionary<string, object> entrantData)
                    {
                        entrants.Add(RaceEntrant.Parse(entrantData));
                    }
                    else throw new InvalidOperationException("The race room contains an invalid player.");
                }
            }

            return new RaceRoom(code, hostUid, hostName, status, seed, length, entrants);
        }

        // Converts the room's entrants into simulation entries, filling empty lanes up to 4 with practice AI dogs.
        public List<DogRaceEntry> BuildSimulationField()
        {
            var field = new List<DogRaceEntry>();
            foreach (var e in Entrants)
            {
                field.Add(new DogRaceEntry(e.PlayerId, e.DisplayName, e.Stats, e.EquippedItemId));
            }

            var aiPool = new[]
            {
                new DogRaceEntry("dash", "Dash", new DogStats { speed = 48, stamina = 30, acceleration = 35 }),
                new DogRaceEntry("pip", "Pip", new DogStats { speed = 35, stamina = 48, acceleration = 48 }),
                new DogRaceEntry("scout", "Scout", new DogStats { speed = 42, stamina = 42, acceleration = 42 })
            };

            int aiIndex = 0;
            while (field.Count < MaxEntrants && aiIndex < aiPool.Length)
            {
                var ai = aiPool[aiIndex++];
                if (!field.Any(f => f.Id == ai.Id)) field.Add(ai);
            }

            return field;
        }
    }
}
