using UnityEngine;

namespace GravityPortals
{
    /// <summary>The one place to change The Tesseract's room and door sizes. The map is built from
    /// it in the editor: change a value, then press Rebuild in this asset's inspector (or Tools >
    /// Gravity Portals > Build The Tesseract).</summary>
    [CreateAssetMenu(menuName = "Gravity Portals/Tesseract Settings", fileName = "TesseractSettings")]
    public class TesseractSettings : ScriptableObject
    {
        public TesseractConfig config = new TesseractConfig();
    }
}
