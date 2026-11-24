using UnityEngine;
using UnityEngine.Rendering;

namespace FlameBlame
{
    /// <summary>Builds simple coloured shapes from code, so the scene needs no hand-made assets.</summary>
    public static class Prim
    {
        static Material baseMaterial;

        /// <summary>
        /// The material every shape is based on. In a player build GameObject.CreatePrimitive gets the old
        /// built-in material, which renders pink under URP, so we use Resources/KeepShader (a URP Lit material)
        /// and fall back to finding a suitable shader by name.
        /// </summary>
        static Material BaseMaterial()
        {
            if (baseMaterial != null) return baseMaterial;
            baseMaterial = Resources.Load<Material>("KeepShader");
            if (baseMaterial == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (s == null) s = Shader.Find("Standard");
                baseMaterial = new Material(s);
            }
            return baseMaterial;
        }

        static GameObject Create(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider); // we use our own grid collision
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            return go;
        }

        public static GameObject Make(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color, string name, bool shadows = true)
        {
            var go = Create(type, parent, localPos, scale, name);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = NewMaterial(color);
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Same as Make, but reuses an existing material (much cheaper for many copies).</summary>
        public static GameObject MakeShared(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name)
        {
            var go = Create(type, parent, localPos, scale, name);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Creates a new material based on the URP base material, with a colour.</summary>
        public static Material NewMaterial(Color color)
        {
            var mat = new Material(BaseMaterial());
            mat.color = color;
            return mat;
        }

        public static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
