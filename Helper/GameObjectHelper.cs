using UnityEngine;

namespace VSFartMod;
public class GameObjectHelper
{
    public static GameObject GetGameObjectCheckFound(string path)
    {
        GameObject go = GameObject.Find(path);
        if (go == null)
        {
            VSFartMod.Logger.LogError(path + " gameobject not found");
        }
        return go;
    }
}

