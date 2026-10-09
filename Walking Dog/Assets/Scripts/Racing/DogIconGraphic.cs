using UnityEngine;
using UnityEngine.UI;

namespace WalkingDog.Racing
{
    // Small vector dog icon built with UI geometry, so it remains crisp at any
    // phone size and can be replaced by an artist's Sprite in a later phase.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DogIconGraphic : MaskableGraphic
    {
        public Color collarColor = new Color(.95f, .65f, .2f);
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Ellipse(mesh, .42f, .50f, .30f, .17f, color);
            Ellipse(mesh, .70f, .70f, .15f, .16f, color);
            Ellipse(mesh, .83f, .65f, .12f, .065f, color);
            Triangle(mesh, new Vector2(.60f,.80f), new Vector2(.60f,.97f), new Vector2(.72f,.83f), color);
            Triangle(mesh, new Vector2(.13f,.51f), new Vector2(.02f,.80f), new Vector2(.25f,.59f), color);
            Box(mesh, .20f, .18f, .29f, .48f, color);
            Box(mesh, .49f, .18f, .58f, .48f, color);
            Box(mesh, .57f, .57f, .73f, .63f, collarColor);
            Ellipse(mesh, .92f, .66f, .035f, .035f, new Color(.15f,.12f,.10f));
            Ellipse(mesh, .74f, .75f, .022f, .025f, new Color(.15f,.12f,.10f));
        }
        private void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            var rect = GetPixelAdjustedRect();
            float width = Mathf.Min(rect.width, rect.height * 1.2f), height = width / 1.2f;
            rect = new Rect(rect.center.x - width / 2, rect.center.y - height / 2, width, height);
            int start = mesh.currentVertCount;
            foreach (var p in new[] { a, b, c }) mesh.AddVert(new Vector3(rect.xMin + p.x * rect.width, rect.yMin + p.y * rect.height), tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
        }
        private void Box(VertexHelper mesh, float x, float y, float x2, float y2, Color tint)
        {
            Triangle(mesh, new Vector2(x,y), new Vector2(x,y2), new Vector2(x2,y2), tint);
            Triangle(mesh, new Vector2(x,y), new Vector2(x2,y2), new Vector2(x2,y), tint);
        }
        private void Ellipse(VertexHelper mesh, float x, float y, float rx, float ry, Color tint)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI / 10, b = (i + 1) * Mathf.PI / 10;
                Triangle(mesh, new Vector2(x,y), new Vector2(x + rx * Mathf.Cos(a), y + ry * Mathf.Sin(a)),
                    new Vector2(x + rx * Mathf.Cos(b), y + ry * Mathf.Sin(b)), tint);
            }
        }
    }
}
