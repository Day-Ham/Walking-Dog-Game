using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;

namespace WalkingDog.Racing
{
    public interface IRaceLobbyService
    {
        Task<RaceRoom> CreateRoomAsync(string hostUid, string hostName, DogStats stats, string equippedItemId, CancellationToken token);
        Task<RaceRoom> JoinRoomAsync(string roomCode, string playerUid, string playerName, DogStats stats, string equippedItemId, CancellationToken token);
        Task LeaveRoomAsync(string roomCode, string playerUid, CancellationToken token);
        Task StartRaceAsync(string roomCode, string hostUid, int seed, CancellationToken token);
        IDisposable ListenRoom(string roomCode, Action<RaceRoom> onUpdate, Action<Exception> onError);
    }

    public sealed class FirebaseRaceLobbyService : IRaceLobbyService
    {
        private readonly FirebaseFirestore db;

        public FirebaseRaceLobbyService(FirebaseFirestore db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<RaceRoom> CreateRoomAsync(string hostUid, string hostName, DogStats stats, string equippedItemId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(hostUid)) throw new ArgumentException("A host needs an authenticated user ID.", nameof(hostUid));
            token.ThrowIfCancellationRequested();

            for (int attempt = 0; attempt < 5; attempt++)
            {
                token.ThrowIfCancellationRequested();
                string candidateCode = RaceRoomCodes.Create(5);
                var docRef = db.Document($"raceRooms/{candidateCode}");

                bool created = await db.RunTransactionAsync(async tx =>
                {
                    var snapshot = await tx.GetSnapshotAsync(docRef);
                    if (snapshot.Exists) return false;

                    var hostEntrant = new RaceEntrant(hostUid, hostName, stats, equippedItemId, isHost: true);
                    var room = new RaceRoom(candidateCode, hostUid, hostName, "waiting", 0, RaceRoom.DefaultLength, new[] { hostEntrant });

                    var data = room.ToDictionary();
                    data["createdAt"] = FieldValue.ServerTimestamp;
                    data["updatedAt"] = FieldValue.ServerTimestamp;

                    tx.Set(docRef, data);
                    return true;
                });

                if (created)
                {
                    var hostEntrant = new RaceEntrant(hostUid, hostName, stats, equippedItemId, isHost: true);
                    return new RaceRoom(candidateCode, hostUid, hostName, "waiting", 0, RaceRoom.DefaultLength, new[] { hostEntrant });
                }
            }

            throw new InvalidOperationException("Could not generate a unique room code. Please try again.");
        }

        public async Task<RaceRoom> JoinRoomAsync(string roomCode, string playerUid, string playerName, DogStats stats, string equippedItemId, CancellationToken token)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Please enter a valid room code.", nameof(roomCode));
            if (string.IsNullOrWhiteSpace(playerUid)) throw new ArgumentException("Player ID is required.", nameof(playerUid));
            token.ThrowIfCancellationRequested();

            var docRef = db.Document($"raceRooms/{normalized}");
            return await db.RunTransactionAsync(async tx =>
            {
                var snapshot = await tx.GetSnapshotAsync(docRef);
                token.ThrowIfCancellationRequested();

                if (!snapshot.Exists) throw new InvalidOperationException($"Room {RaceRoomCodes.Format(normalized)} not found.");
                var existingRoom = RaceRoom.Parse(normalized, snapshot.ToDictionary());

                if (existingRoom.IsClosed) throw new InvalidOperationException("This room is closed.");
                if (existingRoom.IsRacing) throw new InvalidOperationException("This race has already started.");

                var list = existingRoom.Entrants.ToList();
                int existingIndex = list.FindIndex(e => e.PlayerId == playerUid);

                if (existingIndex >= 0)
                {
                    // Refresh existing player's stats/equipment in room
                    bool isHost = list[existingIndex].IsHost;
                    list[existingIndex] = new RaceEntrant(playerUid, playerName, stats, equippedItemId, isHost);
                }
                else
                {
                    if (list.Count >= RaceRoom.MaxEntrants) throw new InvalidOperationException("This race room is full (4/4 dogs).");
                    list.Add(new RaceEntrant(playerUid, playerName, stats, equippedItemId, isHost: false));
                }

                var updatedRoom = new RaceRoom(normalized, existingRoom.HostUid, existingRoom.HostName,
                    existingRoom.Status, existingRoom.Seed, existingRoom.TrackLength, list);

                var data = updatedRoom.ToDictionary();
                data["updatedAt"] = FieldValue.ServerTimestamp;
                tx.Set(docRef, data);

                return updatedRoom;
            });
        }

        public async Task LeaveRoomAsync(string roomCode, string playerUid, CancellationToken token)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(playerUid)) return;
            token.ThrowIfCancellationRequested();

            var docRef = db.Document($"raceRooms/{normalized}");
            try
            {
                await db.RunTransactionAsync(async tx =>
                {
                    var snapshot = await tx.GetSnapshotAsync(docRef);
                    if (!snapshot.Exists) return;

                    var room = RaceRoom.Parse(normalized, snapshot.ToDictionary());
                    if (room.IsClosed) return;

                    var remaining = room.Entrants.Where(e => e.PlayerId != playerUid).ToList();

                    if (room.HostUid == playerUid || remaining.Count == 0)
                    {
                        // Host left or no players remain: close room
                        tx.Update(docRef, new Dictionary<string, object>
                        {
                            ["status"] = "closed",
                            ["updatedAt"] = FieldValue.ServerTimestamp
                        });
                    }
                    else
                    {
                        var updatedRoom = new RaceRoom(normalized, room.HostUid, room.HostName, room.Status, room.Seed, room.TrackLength, remaining);
                        var data = updatedRoom.ToDictionary();
                        data["updatedAt"] = FieldValue.ServerTimestamp;
                        tx.Set(docRef, data);
                    }
                });
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("Error leaving race room: " + ex.Message);
            }
        }

        public async Task StartRaceAsync(string roomCode, string hostUid, int seed, CancellationToken token)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Invalid room code.", nameof(roomCode));
            token.ThrowIfCancellationRequested();

            var docRef = db.Document($"raceRooms/{normalized}");
            await db.RunTransactionAsync(async tx =>
            {
                var snapshot = await tx.GetSnapshotAsync(docRef);
                token.ThrowIfCancellationRequested();

                if (!snapshot.Exists) throw new InvalidOperationException("Room not found.");
                var room = RaceRoom.Parse(normalized, snapshot.ToDictionary());

                if (room.HostUid != hostUid) throw new InvalidOperationException("Only the room host can start the race.");
                if (!room.IsWaiting) throw new InvalidOperationException("Race has already started or closed.");
                if (room.Entrants.Count < 1) throw new InvalidOperationException("Cannot start with no entrants.");

                tx.Update(docRef, new Dictionary<string, object>
                {
                    ["status"] = "racing",
                    ["seed"] = (long)seed,
                    ["updatedAt"] = FieldValue.ServerTimestamp
                });
            });
        }

        public IDisposable ListenRoom(string roomCode, Action<RaceRoom> onUpdate, Action<Exception> onError)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Invalid room code.", nameof(roomCode));

            var docRef = db.Document($"raceRooms/{normalized}");
            var registration = docRef.Listen(snapshot =>
            {
                try
                {
                    if (snapshot != null && snapshot.Exists)
                    {
                        var room = RaceRoom.Parse(normalized, snapshot.ToDictionary());
                        onUpdate?.Invoke(room);
                    }
                    else
                    {
                        onUpdate?.Invoke(null);
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                }
            });

            return new ListenerSubscription(registration);
        }

        private sealed class ListenerSubscription : IDisposable
        {
            private ListenerRegistration reg;
            public ListenerSubscription(ListenerRegistration reg) { this.reg = reg; }
            public void Dispose()
            {
                reg?.Stop();
                reg = null;
            }
        }
    }
}
