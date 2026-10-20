using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Menu.Shop;
using UnityEngine;

namespace AzerothAbilities;

internal static class SourceQuality
{
    internal static int Rolls;
    internal static int Level=>Math.Max(1,(MyPlayer.Instance?.inventory?.playerXp?.level??0)+1);
    // Original offer-kind0 VA145958960..1459589E9. The unexplained kind1
    // extraLuck=.5 is not assigned to spell offers without classification proof.
    internal static float[] Weights(float luck,int level,float extraLuck=0)
    {
        double u=Math.Max(0,1+(double)luck+Math.Min(Math.Max(0,level)*.01,.30)+extraLuck);
        double[] weights={60,25*u,10*u*u,4*u*u*u,u*u*u*u};double total=weights.Sum();
        return weights.Select(w=>(float)(w/total)).ToArray();
    }
    internal static ERarity Roll()
    {
        var weights=Weights(LuckCompatibility.Luck,Level);float value=UnityEngine.Random.value;
        for(int i=0;i<4;i++){value-=weights[i];if(value<0)return (ERarity)(i+1);}return ERarity.Legendary;
    }
}
