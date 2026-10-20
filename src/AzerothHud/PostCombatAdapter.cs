using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Assets.Scripts.Saves___Serialization.Progression.Stats;

namespace AzerothHud;

// Presentation adapter: keeps the game's collected damage, DPS, rewards, quests and confirmation flow.
sealed class PostCombatAdapter
{
    sealed class SpellView {public string Id,Name,Icon;public int Rank;}
    sealed class SavedRow {public DamageSourceEntry Row;public string Name,Level,Damage,Dps,Source;public Texture Icon;public float NativeDamage;}
    sealed class SavedSummary {public DeathStatsUi Ui;public string Name;public Texture Portrait;public readonly PauseSpellInventory Spells=new();}
    static readonly Dictionary<string,SpellView> sources=new(StringComparer.Ordinal);
    static readonly Dictionary<int,SavedRow> damageRows=new();
    readonly Dictionary<int,SavedSummary> summaries=new();
    static Func<string,Texture2D> textures;
    static bool catalogRead;
    RenderTexture retainedPortrait;
    public void RememberPortrait(Texture source)
    {
        // Lock the first actual live source-head render. Later death poses change mesh bounds
        // and the portrait camera may render the actor's torso, which must not replace the head.
        if(source==null || retainedPortrait!=null)return;
        if(retainedPortrait==null)
        {
            retainedPortrait=new RenderTexture(192,192,0,RenderTextureFormat.ARGB32);
            retainedPortrait.name="AzerothResultSourcePortrait";retainedPortrait.hideFlags=HideFlags.DontUnloadUnusedAsset;retainedPortrait.Create();
        }
        // Preserve the last actual source-head render if the scene detaches the actor on death.
        Graphics.Blit(source,retainedPortrait);
    }

    internal static bool IsPostCombat()
    {
        if(GameManager.Instance!=null && GameManager.Instance.isGameOver)return true;
        var death=UiManager.Instance?.deathScreen;
        if(death!=null && (Shown(death.statsWindow)||Shown(death.deadUiWindow)||Shown(death.victoryScreen)))return true;
        foreach(var result in Resources.FindObjectsOfTypeAll<DeathStatsUi>())
            if(result!=null && result.gameObject.scene.IsValid() && result.gameObject.activeInHierarchy)return true;
        return false;
    }
    static bool Shown(GameObject value)=>value!=null&&value.activeInHierarchy;
    internal static void RefreshSources(string[] ids,int[] ranks,string[] names,string[] descriptions,string[] icons,Func<string,Texture2D> loader)
    {
        textures=loader;
        if(!catalogRead)
        {
            catalogRead=true;
            ReadCatalog("AzerothAbilities","mage.json","spells","Azeroth:");
            ReadCatalog("AzerothMageTalents","mage-talents.json","talents","AzerothTalent:");
            if(sources.TryGetValue("AzerothTalent:water_elemental",out var pet))sources["Azeroth:water_elemental"]=pet;
        }
        for(int i=0;i<ids.Length;i++)
        {
            var key="Azeroth:"+ids[i];
            if(!sources.TryGetValue(key,out var spell))sources[key]=spell=new SpellView{Id=ids[i],Name=ids[i],Icon=""};
            if(i<names.Length&&!string.IsNullOrWhiteSpace(names[i]))spell.Name=names[i];
            if(i<icons.Length&&!string.IsNullOrWhiteSpace(icons[i]))spell.Icon=icons[i];
            if(i<ranks.Length)spell.Rank=ranks[i];
        }
    }
    static void ReadCatalog(string assemblyName,string file,string array,string prefix)
    {
        try
        {
            var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==assemblyName);
            if(assembly==null)return;
            var path=Path.Combine(Path.GetDirectoryName(assembly.Location)!,file);
            if(!File.Exists(path))return;
            using var document=JsonDocument.Parse(File.ReadAllText(path));
            if(!document.RootElement.TryGetProperty(array,out var entries))return;
            foreach(var item in entries.EnumerateArray())
            {
                string id=item.GetProperty("id").GetString()??"";
                sources[prefix+id]=new SpellView{Id=id,Name=item.GetProperty("name").GetString()??id,Icon=item.GetProperty("icon").GetString()??"",Rank=0};
            }
        }
        catch(Exception e){Plugin.Current?.Log.LogWarning("Post-combat catalog read: "+e.Message);}
    }
    internal static void AdaptDamageRow(DamageSourceEntry row,DamageSource source)
    {
        if(row==null || source==null || string.IsNullOrEmpty(source.damageSource))return;
        string key=source.damageSource;
        if(!key.StartsWith("Azeroth:",StringComparison.Ordinal)&&!key.StartsWith("AzerothTalent:",StringComparison.Ordinal))return;
        int id=row.GetInstanceID();
        if(!damageRows.TryGetValue(id,out var saved))damageRows[id]=saved=new SavedRow{Row=row,Name=row.t_sourceName?.text,Level=row.t_lvl?.text,Icon=row.icon?.texture,Damage=row.t_dmg?.text,Dps=row.t_dps?.text,Source=key,NativeDamage=source.damage};
        if(sources.TryGetValue(key,out var spell))
        {
            if(row.t_sourceName!=null)row.t_sourceName.text=spell.Name;
            if(row.icon!=null&&textures!=null&&!string.IsNullOrEmpty(spell.Icon))
            {var texture=textures("Interface/Icons/"+spell.Icon+".png");if(texture!=null)row.icon.texture=texture;}
            // Main spells use their actual learned rank; talent sources use the actual run snapshot if available.
            int rank=spell.Rank;
            if(key.StartsWith("AzerothTalent:",StringComparison.Ordinal)||spell.Id=="water_elemental")rank=TalentRunRank(spell.Id);
            if(row.t_lvl!=null)row.t_lvl.text=rank>0?rank.ToString():"—";
        }
        else if(row.t_sourceName!=null)row.t_sourceName.text=key+" (источник не сопоставлен)";
        // Deliberately leave t_dmg and t_dps byte-for-byte as formatted by the native Set method.
        PostCombatDiagnostics.DamageRowsMapped=damageRows.Count;
    }
    static int TalentRunRank(string id)
    {
        try
        {
            var plugin=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.Plugin")).FirstOrDefault(t=>t!=null);
            var state=plugin?.GetField("Run",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null);
            return state?.GetType().GetMethod("Rank")?.Invoke(state,new object[]{id}) is int rank?rank:0;
        }
        catch{return 0;}
    }
    public void Refresh(bool mage,TMP_FontAsset font,Material material,Texture portrait,string[] ids,int[] ranks,string[] names,string[] descriptions,string[] icons,Func<string,Texture2D> loader)
    {
        if(!mage){Restore();return;}
        RefreshSources(ids,ranks,names,descriptions,icons,loader);
        if(retainedPortrait!=null)portrait=retainedPortrait;
        PostCombatDiagnostics.Visible=true;PostCombatDiagnostics.CombatHudHidden=true;
        foreach(var ui in Resources.FindObjectsOfTypeAll<DeathStatsUi>())
        {
            if(ui==null || !ui.gameObject.scene.IsValid() || !ui.gameObject.activeInHierarchy)continue;
            int id=ui.GetInstanceID();
            if(!summaries.TryGetValue(id,out var saved))summaries[id]=saved=new SavedSummary{Ui=ui,Name=ui.t_characterName?.text,Portrait=ui.i_character?.texture};
            if(ui.t_characterName!=null)ui.t_characterName.text="Маг";
            if(ui.i_character!=null&&portrait!=null)ui.i_character.texture=portrait;
            saved.Spells.RefreshRow(ui.weaponsParent,font,material,ids,ranks,names,descriptions,icons,loader);
            saved.Spells.PlaceAboveBooks(ui.tomesParent,UiManager.Instance?.deathScreen?.statsWindow?.transform??ui.transform);
            PostCombatDiagnostics.SourcePortraitShown=ui.i_character!=null&&portrait!=null&&ui.i_character.texture==portrait;
            PostCombatDiagnostics.ResultSpellCount=ids.Length;
        }
        // A native panel can rebuild rows after activation; reapply identity without changing its numbers.
        foreach(var saved in damageRows.Values.ToArray())
        {
            if(saved.Row==null)continue;
            var native=RunStats.damageSources;
            if(native!=null&&native.ContainsKey(saved.Source))AdaptDamageRow(saved.Row,native[saved.Source]);
        }
    }
    public void Enforce(){foreach(var saved in summaries.Values)saved.Spells.Enforce();}
    public void SetScroll(float value){foreach(var saved in summaries.Values)saved.Spells.SetScroll(value);}
    public void Restore()
    {
        foreach(var saved in summaries.Values)
        {
            saved.Spells.Dispose();if(saved.Ui==null)continue;
            if(saved.Ui.t_characterName!=null)saved.Ui.t_characterName.text=saved.Name;
            if(saved.Ui.i_character!=null)saved.Ui.i_character.texture=saved.Portrait;
        }
        summaries.Clear();
        foreach(var saved in damageRows.Values)
        {
            if(saved.Row==null)continue;
            if(saved.Row.t_sourceName!=null)saved.Row.t_sourceName.text=saved.Name;
            if(saved.Row.t_lvl!=null)saved.Row.t_lvl.text=saved.Level;
            if(saved.Row.icon!=null)saved.Row.icon.texture=saved.Icon;
        }
        damageRows.Clear();PostCombatDiagnostics.Visible=false;PostCombatDiagnostics.CombatHudHidden=false;PostCombatDiagnostics.SourcePortraitShown=false;PostCombatDiagnostics.DamageRowsMapped=0;PostCombatDiagnostics.ResultSpellCount=0;
    }
    public void Dispose(){Restore();if(retainedPortrait!=null){retainedPortrait.Release();UnityEngine.Object.Destroy(retainedPortrait);}retainedPortrait=null;sources.Clear();catalogRead=false;textures=null;}
    internal static string[] DescribeDamage()=>damageRows.Values.Select(s=>$"source={s.Source};nativeDamage={s.NativeDamage:R};name={s.Row?.t_sourceName?.text};level={s.Row?.t_lvl?.text};icon={s.Row?.icon?.texture?.name};sourceIconMatches={sources.TryGetValue(s.Source,out var view)&&s.Row?.icon?.texture!=null&&string.Equals(s.Row.icon.texture.name,view.Icon,StringComparison.OrdinalIgnoreCase)};damageText={s.Row?.t_dmg?.text};dpsText={s.Row?.t_dps?.text};nativeDamageTextUnchanged={s.Row!=null&&s.Row.t_dmg?.text==s.Damage};nativeDpsTextUnchanged={s.Row!=null&&s.Row.t_dps?.text==s.Dps}").ToArray();
    internal string[] DescribeInventory(){var output=new List<string>();foreach(var summary in summaries.Values)output.AddRange(summary.Spells.Describe());return output.ToArray();}
}

[HarmonyPatch(typeof(DamageSourceEntry),nameof(DamageSourceEntry.Set))]
static class PostCombatDamageRowPatch
{
    static void Postfix(DamageSourceEntry __instance,DamageSource dmgSource)
    {try{PostCombatAdapter.AdaptDamageRow(__instance,dmgSource);}catch(Exception e){Plugin.Current?.Log.LogWarning("Post-combat row adaptation: "+e.Message);}}
}

public static class PostCombatDiagnostics
{
    public static bool Visible{get;internal set;}
    public static bool CombatHudHidden{get;internal set;}
    public static bool SourcePortraitShown{get;internal set;}
    public static int DamageRowsMapped{get;internal set;}
    public static int ResultSpellCount{get;internal set;}
    public static string ResultViewport{get;internal set;}="";
    public static string[] DescribeDamage()=>PostCombatAdapter.DescribeDamage();
}
