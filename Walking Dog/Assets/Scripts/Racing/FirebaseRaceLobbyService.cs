using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore;
using Firebase.Auth;

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
        private string CurrentUser => FirebaseAuth.DefaultInstance.CurrentUser?.UserId ?? "";
        // Verifies caller identity and cancellation state before lobby actions.
        private void Check(string uid, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(uid) || CurrentUser != uid) throw new OperationCanceledException();
        }

        // Reads entrant dog stats, display name, and equipped collar from Firestore.
        private async Task<RaceEntrant> ReadEntrantAsync(Transaction tx, string uid, string name, bool host, CancellationToken token)
        {
            var dog = await tx.GetSnapshotAsync(db.Document($"users/{uid}/dog/main"));
            var profile = await tx.GetSnapshotAsync(db.Document($"leaderboardProfiles/{uid}"));
            var inventory = await tx.GetSnapshotAsync(db.Document($"users/{uid}/inventory/main"));
            Check(uid, token);
            if (!dog.Exists) throw new InvalidOperationException("Load your dog before entering a race room.");
            if (profile.Exists && profile.TryGetValue("displayName", out string savedName)) name = savedName;
            name = string.IsNullOrWhiteSpace(name) ? "Racer" : name.Trim();
            if (name.Length > 64) name = name.Substring(0, 64);
            string equipment = inventory.Exists && inventory.TryGetValue("equippedItemId", out string item) ? item : "";
            return new RaceEntrant(uid, name, DogStats.Parse(dog.ToDictionary()), equipment, host);
        }

        // Initializes race lobby service with Firestore database instance.
        public FirebaseRaceLobbyService(FirebaseFirestore db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // Generates a room code and creates a new waiting multiplayer race room document in Firestore.
        public async Task<RaceRoom> CreateRoomAsync(string hostUid, string hostName, DogStats stats, string equippedItemId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(hostUid)) throw new ArgumentException("A host needs an authenticated user ID.", nameof(hostUid));
            Check(hostUid, token);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                token.ThrowIfCancellationRequested();
                string candidateCode = RaceRoomCodes.Create(5);
                var docRef = db.Document($"raceRooms/{candidateCode}");

                var created = await RunTransactionAsync(async tx =>
                {
                    var snapshot = await tx.GetSnapshotAsync(docRef);
                    Check(hostUid, token);
                    if (snapshot.Exists) return null;

                    var hostEntrant = await ReadEntrantAsync(tx, hostUid, hostName, true, token);
                    var room = new RaceRoom(candidateCode, hostUid, hostEntrant.DisplayName, "waiting", 0, RaceRoom.DefaultLength, new[] { hostEntrant });

                    var data = room.ToDictionary();
                    data["createdAt"] = FieldValue.ServerTimestamp;
                    data["updatedAt"] = FieldValue.ServerTimestamp;

                    tx.Set(docRef, data);
                    return room;
                }, token);

                if (created != null)
                {
                    // A native commit can finish after Back/timeout. Close that
                    // otherwise orphaned room while the same owner is signed in.
                    if (token.IsCancellationRequested || CurrentUser != hostUid)
                    {
                        if (CurrentUser == hostUid) await LeaveRoomAsync(candidateCode, hostUid, CancellationToken.None);
                        throw new OperationCanceledException();
                    }
                    return created;
                }
            }

            throw new InvalidOperationException("Could not generate a unique room code. Please try again.");
        }

        // Adds a player to an open waiting race room or updates their existing stats in the room.
        public async Task<RaceRoom> JoinRoomAsync(string roomCode, string playerUid, string playerName, DogStats stats, string equippedItemId, CancellationToken token)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Please enter a valid room code.", nameof(roomCode));
            if (string.IsNullOrWhiteSpace(playerUid)) throw new ArgumentException("Player ID is required.", nameof(playerUid));
            Check(playerUid, token);

            var docRef = db.Document($"raceRooms/{normalized}");
            var joined = await RunTransactionAsync(async tx =>
            {
                var snapshot = await tx.GetSnapshotAsync(docRef);
                Check(playerUid, token);

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
                    list[existingIndex] = await ReadEntrantAsync(tx, playerUid, playerName, isHost, token);
                }
                else
                {
                    if (list.Count >= RaceRoom.MaxEntrants) throw new InvalidOperationException("This race room is full (4/4 dogs).");
                    list.Add(await ReadEntrantAsync(tx, playerUid, playerName, false, token));
                }

                var updatedRoom = new RaceRoom(normalized, existingRoom.HostUid, existingRoom.HostName,
                    existingRoom.Status, existingRoom.Seed, existingRoom.TrackLength, list);

                var data = updatedRoom.ToDictionary();
                data["updatedAt"] = FieldValue.ServerTimestamp;
                tx.Update(docRef, data); // Preserve createdAt across joins and refreshes.

                return updatedRoom;
            }, token);
            if (token.IsCancellationRequested || CurrentUser != playerUid)
            {
                if (CurrentUser == playerUid) await LeaveRoomAsync(normalized, playerUid, CancellationToken.None);
                throw new OperationCanceledException();
            }
            return joined;
        }

        // Removes a player from the room or closes the room if the host departs.
        public async Task LeaveRoomAsync(string roomCode, string playerUid, CancellationToken token)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(playerUid)) return;
            Check(playerUid, token);

            var docRef = db.Document($"raceRooms/{normalized}");
            try
            {
                await db.RunTransactionAsync(async tx =>
                {
                    var snapshot = await tx.GetSnapshotAsync(docRef);
                    Check(playerUid, token);
                    if (!snapshot.Exists) return;

                    var room = RaceRoom.Parse(normalized, snapshot.ToDictionary());
                    if (room.IsClosed) return;
                    if (!room.Entrants.Any(e => e.PlayerId == playerUid)) return;
                    if (room.IsRacing && room.HostUid != playerUid) return; // The racing roster is frozen.

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
                        tx.Update(docRef, data);
                    }
                });
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("Error leaving race room: " + ex.Message);
            }
        }

        // Retries Firestore transactions on transient permission denied errors.
        private async Task<T> RunTransactionAsync<T>(Func<Transaction, Task<T>> operation, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return await db.RunTransactionAsync(operation); }
                catch (Firebase.FirebaseException error) when (error.ErrorCode == (int)FirestoreError.PermissionDenied && attempt < 2)
                { await Task.Delay(100 * (attempt + 1), token); }
            }
        }

        // Transitions room status to racing and sets the shared random seed for synchronous simulation.
        public async Task StartRaceAsync(string roomCode, string hostUid, int seed, CancellationToken token)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Invalid room code.", nameof(roomCode));
            Check(hostUid, token);

            var docRef = db.Document($"raceRooms/{normalized}");
            await db.RunTransactionAsync(async tx =>
            {
                var snapshot = await tx.GetSnapshotAsync(docRef);
                Check(hostUid, token);

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

        // Subscribes to real-time snapshot updates on a Firestore race room document.
        public IDisposable ListenRoom(string roomCode, Action<RaceRoom> onUpdate, Action<Exception> onError)
        {
            string normalized = RaceRoomCodes.Normalize(roomCode);
            if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Invalid room code.", nameof(roomCode));

            var docRef = db.Document($"raceRooms/{normalized}");
            string listenerOwner = CurrentUser;
            if (string.IsNullOrEmpty(listenerOwner)) throw new InvalidOperationException("Sign in to follow a race room.");
            var subscription = new ListenerSubscription();
            var registration = docRef.Listen(snapshot =>
            {
                if (subscription.Stopped || CurrentUser != listenerOwner) return;
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

            subscription.Attach(registration);
            ObserveListener(registration.ListenerTask, subscription, onError);
            return subscription;
        }

        // Monitors the underlying listener background task and routes errors to the callback.
        private static async void ObserveListener(Task task, ListenerSubscription subscription, Action<Exception> onError)
        {
            try { await task; }
            catch (Exception error) { if (!subscription.Stopped) onError?.Invoke(error); }
        }

        private sealed class ListenerSubscription : IDisposable
        {
            private ListenerRegistration reg;
            public bool Stopped { get; private set; }
            // Binds an active Firestore registration to this subscription tracker.
            public void Attach(ListenerRegistration registration) { reg = registration; if (Stopped) reg.Stop(); }
            // Stops listening to document updates and releases Firestore listener resources.
            public void Dispose()
            {
                Stopped = true;
                reg?.Stop();
                reg = null;
            }
        }
    }
}
