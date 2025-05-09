using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;

namespace VSFartMod.Helper;

public class FindFsmString
{
    public static void SearchAllGameObjects()
    {
        // Get all GameObjects in the scene
        GameObject[] allGameObjects = VSFartMod.FindObjectsOfType<GameObject>();
        Debug.Log($"Trying my best boss!");

        foreach (GameObject gameObject in allGameObjects)
        {
            // Get all PlayMakerFSM components on the GameObject
            PlayMakerFSM[] fsms = gameObject.GetComponents<PlayMakerFSM>();

            foreach (PlayMakerFSM fsm in fsms)
            {
                // Get ArrayVariables from the FsmVariables
                FsmArray[] arrayVariables = fsm.FsmVariables.ArrayVariables;

                foreach (FsmArray arrayVariable in arrayVariables)
                {
                    // Ensure the array holds strings
                    if (arrayVariable.ElementType == VariableType.String)
                    {
                        foreach (var value in arrayVariable.Values)
                        {
                            if (value is string strValue && strValue.ToLower().Contains("such a"))
                            {
                                string fullPath = GetFullPath(gameObject);
                                Debug.Log($"Found string in GameObject '{fullPath}': {strValue}");
                            }
                        }
                    }
                }
            }
        }
    }


    static string GetFullPath(GameObject gameObject)
    {
        string path = gameObject.name;
        Transform parent = gameObject.transform.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
}