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
        private LeaderboardPanelUI owner;
        private IFriendsService service;
        private TMP_Text message, code;
        private TMP_InputField input;
        private Button send, refresh, copy, buttonSource;
        private TMP_Text textSource;
        private RectTransform list;
        private ScrollRect scroll;
        private readonly List<GameObject> rows = new List<GameObject>();
        private CancellationTokenSource pending;
        private string observedUser = "";
        private int generation;
        private bool busy;

        internal static FriendsPanelUI Create(Transform parent, LeaderboardPanelUI owner, Button source, TMP_Text text, TMP_InputField inputSource)
        {
            var root = new GameObject("Friends manager", typeof(RectTransform), typeof(Image));
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            Place(root.transform, .04f, .015f, .96f, .91f);
            root.GetComponent<Image>().color = new Color(.07f, .065f, .14f, 1);
            var panel = root.AddComponent<FriendsPanelUI>();
            panel.owner = owner;
            panel.buttonSource = source;
            panel.textSource = text;
            MakeText(root.transform, text, "Friends title", "Friends", .06f, .92f, .64f, .985f, 44);
            var close = MakeButton(root.transform, source, "Close friends", "Rankings", .65f, .925f, .95f, .98f);
            close.onClick.AddListener(() => { root.SetActive(false); owner.OnRefresh(); });
            MakeText(root.transform, text, "Code hint", "Your friend code · share it with a friend", .06f, .86f, .94f, .91f, 28);
            panel.code = MakeText(root.transform, text, "Friend code", "", .06f, .80f, .73f, .86f, 28);
            panel.copy = MakeButton(root.transform, source, "Copy code", "Copy", .76f, .80f, .94f, .86f);
            panel.copy.onClick.AddListener(() => { GUIUtility.systemCopyBuffer = panel.observedUser; panel.message.text = "Friend code copied."; });
            panel.input = Instantiate(inputSource, root.transform);
            panel.input.name = "Enter friend code";
            Place(panel.input.transform, .06f, .71f, .70f, .78f);
            panel.input.characterLimit = 128;
            panel.input.onValueChanged = new TMP_InputField.OnChangeEvent();
            panel.input.onEndEdit = new TMP_InputField.SubmitEvent();
            panel.input.SetTextWithoutNotify("");
            var placeholder = MakeText(panel.input.transform, text, "Friend code placeholder", "Paste friend's code", 0, 0, 1, 1, 28);
            placeholder.color = new Color(.7f, .7f, .75f);
            panel.input.placeholder = placeholder;
            panel.send = MakeButton(root.transform, source, "Send request", "Send", .73f, .71f, .94f, .78f);
            panel.send.onClick.AddListener(() => { _ = panel.RunAsync(panel.input.text.Trim(), FriendAction.Send); });
            panel.message = MakeText(root.transform, text, "Friends status", "", .06f, .595f, .94f, .695f, 28);
            var viewport = new GameObject("Friends list", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(root.transform, false);
            Place(viewport.transform, .04f, .10f, .96f, .585f);
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, .08f);
            panel.scroll = viewport.GetComponent<ScrollRect>();
            panel.scroll.horizontal = false;
            panel.scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = new GameObject("Friend rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            panel.list = content.GetComponent<RectTransform>();
            panel.list.anchorMin = new Vector2(0, 1);
            panel.list.anchorMax = new Vector2(1, 1);
            panel.list.pivot = new Vector2(.5f, 1);
            panel.list.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.scroll.content = panel.list;
            panel.scroll.viewport = viewport.GetComponent<RectTransform>();
            panel.refresh = MakeButton(root.transform, source, "Refresh friends", "Refresh friends", .20f, .02f, .80f, .08f);
            panel.refresh.onClick.AddListener(() => { _ = panel.RunAsync(); });
            return panel;
        }

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
                code.text = observedUser;
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
                MakeText(row.transform, textSource, "Friend name", entry.DisplayName + " · " + state, .03f, .50f, .97f, .98f, 30);
                if (incoming)
                {
                    var accept = MakeButton(row.transform, buttonSource, "Accept", "Accept", .04f, .04f, .48f, .46f);
                    accept.onClick.AddListener(() => { _ = RunAsync(entry.PlayerId, FriendAction.Accept); });
                }
                var remove = MakeButton(row.transform, buttonSource, "Remove", entry.Accepted ? "Remove friend" : incoming ? "Decline" : "Cancel request",
                    incoming ? .52f : .20f, .04f, incoming ? .96f : .80f, .46f);
                remove.onClick.AddListener(() => { _ = RunAsync(entry.PlayerId, FriendAction.Remove); });
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
    }
}
