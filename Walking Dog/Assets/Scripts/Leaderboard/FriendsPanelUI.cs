using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WalkingDog.Leaderboards
{
    public sealed class FriendsPanelUI : MonoBehaviour
    {
       
        [SerializeField] private LeaderboardPanelUI owner;
        private IFriendsService service;
        [SerializeField] private TMP_Text message, code;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private Button send, refresh, copy;
        [SerializeField] private RectTransform list;
        [SerializeField] private ScrollRect scroll;

      // what do the source do
        [SerializeField] private Button buttonSource;
        [SerializeField] private TMP_Text textSource;
        private readonly List<GameObject> rows = new List<GameObject>();
        private CancellationTokenSource pending;
        private string observedUser = "";
        private int generation;
        private bool busy;

      
      

        
        private static TMP_Text MakeText(Transform parent, TMP_Text source, string name, string value, float left, float bottom, float right, float top, float size)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(parent, false);
            Place(label.transform, left, bottom, right, top);
            label.font = source.font; label.fontSize = size; label.fontSizeMin = 20; label.fontSizeMax = size;
            label.enableAutoSizing = true; label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white; label.richText = false; label.raycastTarget = false; label.text = value;
            return label;
        }

      
        internal static void Place(Transform transform, float left, float bottom, float right, float top)
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(left, bottom); rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

    
        internal static Button MakeButton(Transform parent, Button source, string name, string label, float left, float bottom, float right, float top)
        {
            var button = Instantiate(source, parent);
            button.name = name;
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            Place(button.transform, left, bottom, right, top);
            var text = button.GetComponentInChildren<TMP_Text>();
            text.text = label; text.enableAutoSizing = true; text.fontSizeMin = 20; text.fontSizeMax = 34;
            return button;
        }


        #region KEEP — CONNECT THESE TO INSPECTOR BUTTON EVENTS

        // Close friends button -> CloseFriendsPanel
        public void CloseFriendsPanel()
        {
            gameObject.SetActive(false);
            owner.OnRefresh();
        }

        // Copy code button -> CopyFriendCode
        public void CopyFriendCode()
        {
            if (busy || string.IsNullOrEmpty(code.text)) return;
            GUIUtility.systemCopyBuffer = code.text;
            message.text = "Friend code copied.";
        }

        // Send request button -> SendFriendRequest
        public void SendFriendRequest() => _ = RunAsync(input.text.Trim(), FriendAction.Send);

        // Refresh friends button -> RefreshFriends
        public void RefreshFriends() => _ = RunAsync();

        // KEEP while rows are runtime-created. A player id is required, so these cannot
        // be assigned directly to a normal Inspector Button event without row binding.
        private void AcceptFriendRequest(string playerId) => _ = RunAsync(playerId, FriendAction.Accept);
        private void RemoveFriend(string playerId) => _ = RunAsync(playerId, FriendAction.Remove);
        #endregion

        #region KEEP — PANEL LIFECYCLE, DATA, AND DYNAMIC FRIEND ROWS


        private void OnEnable() { if (owner != null) _ = RunAsync(); }
        private void Update()
        {
            if (service != null && service.AuthenticatedUserId != observedUser)
            {
                observedUser = service.AuthenticatedUserId;
                input.SetTextWithoutNotify("");
                _ = RunAsync();
            }
        }

        private async Task RunAsync(string target = null, FriendAction action = FriendAction.Send)
        {
            if (target != null && busy) return;
            Cancel();
            var request = generation;
            pending = new CancellationTokenSource();
            var token = pending.Token;
            busy = true;
            ClearRows();
            code.text = "";
            message.text = target == null ? "Loading friends…" : "Updating friendship…";
            SetButtons();
            try
            {
                var store = await owner.GetServiceAsync();
                if (!Current(request)) return;
                service = store as IFriendsService;
                if (service == null) throw new InvalidOperationException("Friends service unavailable.");
                observedUser = service.AuthenticatedUserId;
                if (string.IsNullOrEmpty(observedUser)) { message.text = "Sign in to manage friends."; return; }
                if (target != null) await service.ChangeFriendAsync(target, action, token);
                if (!Current(request)) return;
                var entries = await service.LoadFriendsAsync(token);
                if (!Current(request) || observedUser != service.AuthenticatedUserId) return;
                var friendCode = await service.GetFriendCodeAsync(token);
                if (!Current(request) || observedUser != service.AuthenticatedUserId) return;
                code.text = friendCode;
                Render(entries);
                if (target != null) input.SetTextWithoutNotify("");
                message.text = target != null ? action == FriendAction.Send ? "Request sent. Your friend must accept to join your rankings."
                    : action == FriendAction.Accept ? "Request accepted. See your Friends rankings." : "Friendship or request removed."
                    : entries.Count == 0 ? "No friends yet. Exchange codes and send your first request." : "Accept incoming requests to compare rankings. Pull up the list to see more.";
            }
            catch (OperationCanceledException) { }
            catch (FriendRequestException error) { if (Current(request)) message.text = error.Message + " Tap Refresh friends."; }
            catch (Exception error)
            {
                if (Current(request)) message.text = "Couldn't update friends. Check your connection, then Refresh friends to check the result.";
                Debug.LogWarning("Friends request failed: " + error.GetType().Name);
            }
            finally { if (Current(request)) { busy = false; SetButtons(); } }
        }

        // KEEP: rows reflect server data and must remain dynamic unless replaced with
        // a FriendRowUI prefab or a pooling implementation.
        private void Render(IReadOnlyList<FriendEntry> entries)
        {
            ClearRows();
            foreach (var entry in entries)
            {
                var row = new GameObject("Friend " + entry.PlayerId, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                row.transform.SetParent(list, false);
                row.GetComponent<Image>().color = new Color(.16f, .14f, .26f, 1);
                row.GetComponent<LayoutElement>().preferredHeight = 150;
                rows.Add(row);
                bool incoming = !entry.Accepted && entry.RequestedBy != observedUser;
                string state = entry.Accepted ? "Friends" : incoming ? "Incoming request" : "Request sent";
                var photo = new GameObject("Profile photo", typeof(RectTransform), typeof(Image), typeof(ProfilePhotoUI));
                photo.transform.SetParent(row.transform, false);
                Place(photo.transform, .025f, .53f, .15f, .96f);
                photo.GetComponent<ProfilePhotoUI>().Bind(entry.PlayerId, entry.DisplayName, entry.PhotoUrl, textSource.font);
                MakeText(row.transform, textSource, "Friend name", entry.DisplayName + " · " + state, .18f, .50f, .97f, .98f, 30);
                if (incoming)
                {
                    var accept = MakeButton(row.transform, buttonSource, "Accept", "Accept", .04f, .04f, .48f, .46f);
                    accept.onClick.AddListener(() => AcceptFriendRequest(entry.PlayerId));
                }
                var remove = MakeButton(row.transform, buttonSource, "Remove", entry.Accepted ? "Remove friend" : incoming ? "Decline" : "Cancel request",
                    incoming ? .52f : .20f, .04f, incoming ? .96f : .80f, .46f);
                remove.onClick.AddListener(() => RemoveFriend(entry.PlayerId));
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }

        private void SetButtons()
        {
            bool signedIn = service != null && !string.IsNullOrEmpty(service.AuthenticatedUserId);
            refresh.interactable = !busy;
            input.interactable = send.interactable = !busy && signedIn;
            copy.interactable = !busy && signedIn && !string.IsNullOrEmpty(code.text);
        }
        private bool Current(int request) => this != null && isActiveAndEnabled && request == generation;
        private void Cancel() { generation++; pending?.Cancel(); pending?.Dispose(); pending = null; busy = false; }
        private void ClearRows()
        {
            foreach (var row in rows) if (row != null) { row.SetActive(false); if (Application.isPlaying) Destroy(row); else DestroyImmediate(row); }
            rows.Clear();
        }
        private void OnDisable() { Cancel(); ClearRows(); if (code != null) code.text = ""; if (input != null) input.SetTextWithoutNotify(""); }

        #endregion

    }
}
