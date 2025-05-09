using System.IO;
using UnityEngine;

namespace VSFartMod;
public class Assets
{
    static AssetBundle assets;
    //public static GameObject hair;
    public static Texture2D NewLogo;
    public static TextAsset ListOfToggles;
    public static GameObject FartingToggle;

    //public static GameObject background;
    public static void LoadAssets()
    {
        //Find where the asset bundle is, and load it.
        //PUT THE NAME OF YOUR ASSET BUNDLE
        string assetPath = Path.Combine(Application.streamingAssetsPath, "vsfartmod");
        VSFartMod.Logger.LogInfo("Loading assets from " + assetPath);
        assets = AssetBundle.LoadFromFile(assetPath);

        //3D Prefab called "hair"
        //hair = assets.LoadAsset<GameObject>("Hair");
        //2D texture called "RedEye"
        NewLogo = assets.LoadAsset<Texture2D>("NewLogo.png");
        ListOfToggles = assets.LoadAsset<TextAsset>("ListOfToggles.txt");
        FartingToggle = assets.LoadAsset<GameObject>("Farting _ Farting");

        //3D Prefab called "Prison"
        //background = assets.LoadAsset<GameObject>("Prison");
        if (ListOfToggles != null)
            VSFartMod.Logger.LogInfo("Loaded ListOfToggles successfully.");
        else
            VSFartMod.Logger.LogWarning("ListOfToggles not found in asset bundle.");
    }
}
