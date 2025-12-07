using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace RimLife
{
    /// <summary>
    /// Debug helper: adds a dev-mode gizmo on selected Pawn to test the TextGenerator with a pawn's bio.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class LinguisticDebug
    {
        static LinguisticDebug()
        {
            // Ensure Harmony patch for gizmo injection is applied.
            var harmony = new Harmony("RimLife.LinguisticDebug");
            harmony.PatchAll();
        }
    }
}
