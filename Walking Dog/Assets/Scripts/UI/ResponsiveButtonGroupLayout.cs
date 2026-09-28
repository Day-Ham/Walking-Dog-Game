using UnityEngine;

namespace WalkingDog.Runtime
{
    /// <summary>
    /// Keeps the walk-screen action buttons aligned to the 1080-wide design area
    /// while the Canvas expands for tablets and other wide displays.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ResponsiveButtonGroupLayout : MonoBehaviour
    {
        private static readonly string[] ButtonNames =
        {
            "History Button",
            "Leaderboard Button",
            "Gacha button",
            "Inventory button",
            "Log Out Button"
        };

        private const float DesignRightEdge = 504f;
        private const float TopMargin = 36f;
        private const float ButtonWidth = 300f;
        private const float ButtonHeight = 74f;
        private const float ButtonSpacing = 18f;

        private void OnEnable() => ApplyLayout();
        private void OnValidate() => ApplyLayout();
        private void OnTransformChildrenChanged() => ApplyLayout();

        private void ApplyLayout()
        {
            var group = (RectTransform)transform;
            group.anchorMin = new Vector2(0.5f, 1f);
            group.anchorMax = new Vector2(0.5f, 1f);
            group.pivot = new Vector2(1f, 1f);
            group.anchoredPosition = new Vector2(DesignRightEdge, -TopMargin);
            group.sizeDelta = new Vector2(ButtonWidth, ButtonNames.Length * ButtonHeight + (ButtonNames.Length - 1) * ButtonSpacing);

            for (var i = 0; i < ButtonNames.Length; i++)
            {
                var button = transform.Find(ButtonNames[i]) as RectTransform;
                if (button == null) continue;

                button.anchorMin = new Vector2(0.5f, 1f);
                button.anchorMax = new Vector2(0.5f, 1f);
                button.pivot = new Vector2(0.5f, 1f);
                button.anchoredPosition = new Vector2(0f, -i * (ButtonHeight + ButtonSpacing));
                button.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            }
        }
    }
}
