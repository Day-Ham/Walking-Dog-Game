using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace WalkingDog.Leaderboards
{
    internal static class ProfilePhotos
    {
        internal const string UploadPrefix = "data:image/jpeg;base64,";

        // Checks whether a string represents a valid base-64 encoded JPEG data URL.
        internal static bool IsUpload(string value)
        {
            if (value == null || value.Length > 32768 || !value.StartsWith(UploadPrefix + "/9j/", StringComparison.Ordinal)) return false;
            try { Convert.FromBase64String(value.Substring(UploadPrefix.Length)); return true; }
            catch (FormatException) { return false; }
        }

        // Sanitizes a photo URL to only allow valid JPEG data URLs or trusted Google user content links.
        internal static string Normalize(string value)
            => IsUpload(value) ? value : value != null && value.Length <= 2048
                && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
                && uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)
                ? value : "";

        // Generates up to two capital initials from a player's display name.
        internal static string Initials(string name)
        {
            var words = (name ?? "").Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0 ? "?" : (words[0].Substring(0, 1)
                + (words.Length > 1 ? words[words.Length - 1].Substring(0, 1) : "")).ToUpperInvariant();
        }
    }

    // Shared by leaderboard cards and friend/request rows; no scene migration required.
    [RequireComponent(typeof(Image))]
    public sealed class ProfilePhotoUI : MonoBehaviour
    {
        private Image portrait;
        private TMP_Text initials;
        private string url = "";
        private Coroutine loading;
        private UnityWebRequest request;
        private Sprite sprite;
        private Texture2D texture;
        private Color fallbackColor;

        // Binds player ID, display name, and photo URL to render initials or load photo.
        public void Bind(string playerId, string displayName, string photoUrl, TMP_FontAsset font)
        {
            Release();
            portrait = GetComponent<Image>();
            portrait.sprite = null;
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            // Stable fallback colour derived from identity, independent of runtime hash randomization.
            uint hash = 2166136261;
            foreach (char c in playerId ?? "") hash = (hash ^ c) * 16777619;
            portrait.color = fallbackColor = Color.HSVToRGB((hash % 360) / 360f, .45f, .65f);
            if (initials == null)
            {
                initials = new GameObject("Profile initials", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
                initials.transform.SetParent(transform, false);
                FriendsPanelUI.Place(initials.transform, 0, 0, 1, 1);
                initials.alignment = TextAlignmentOptions.Center;
                initials.enableAutoSizing = true;
                initials.fontSizeMin = 16;
                initials.fontSizeMax = 90;
                initials.raycastTarget = false;
                initials.richText = false;
            }
            initials.font = font;
            initials.color = Color.white;
            initials.text = ProfilePhotos.Initials(displayName);
            initials.gameObject.SetActive(true);
            url = ProfilePhotos.Normalize(photoUrl);
            if (isActiveAndEnabled) Load();
        }

        // Triggers texture decoding or remote download when enabled.
        private void OnEnable() => Load();

        // Initiates image decoding from data URL or downloads remote photo URL via coroutine.
        private void Load()
        {
            if (portrait != null && texture == null && ProfilePhotos.IsUpload(url))
            {
                texture = new Texture2D(2, 2);
                if (texture.LoadImage(Convert.FromBase64String(url.Substring(ProfilePhotos.UploadPrefix.Length)), true)
                    && texture.width <= 192 && texture.height <= 192) ShowTexture();
                else { DisposeImage(texture); texture = null; }
                return;
            }
            if (Application.isPlaying && portrait != null && loading == null && url.Length > 0)
                loading = StartCoroutine(Download());
        }

        // Downloads texture bytes over HTTP and displays texture on success.
        private IEnumerator Download()
        {
            request = UnityWebRequestTexture.GetTexture(url, true);
            request.timeout = 12;
            request.redirectLimit = 0;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                texture = DownloadHandlerTexture.GetContent(request);
                ShowTexture();
            }
            request.Dispose();
            request = null;
            loading = null;
        }

        // Applies texture to image component and hides fallback initials.
        private void ShowTexture()
        {
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            portrait.sprite = sprite;
            portrait.color = Color.white;
            initials.gameObject.SetActive(false);
        }

        // Releases textures and cancels downloads when disabled.
        private void OnDisable() => Release();

        // Cancels active web requests and destroys allocated texture and sprite instances.
        private void Release()
        {
            if (loading != null) StopCoroutine(loading);
            loading = null;
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (portrait != null) { portrait.sprite = null; portrait.color = fallbackColor; }
            if (sprite != null) DisposeImage(sprite);
            if (texture != null) DisposeImage(texture);
            sprite = null;
            texture = null;
            if (initials != null) initials.gameObject.SetActive(true);
        }

        // Destroys a Unity texture or sprite object cleanly.
        private static void DisposeImage(UnityEngine.Object image)
        {
            if (Application.isPlaying) Destroy(image); else DestroyImmediate(image);
        }
    }
}
