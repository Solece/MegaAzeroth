using System.Reflection;
using System.Runtime.InteropServices;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using XpUtility = Inventory__Items__Pickups.XpUtility;

namespace AzerothHud;

[BepInPlugin("local.azerothmegabonk.hud", "Azeroth HUD", "0.1.1")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Current;
    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<float> Scale = null!;
    internal string AssetRoot = "";
    public override void Load()
    {
        Current = this;
        Enabled = Config.Bind("HUD", "Enabled", true, "Show the Azeroth-style gameplay overlay. Native encounter windows remain available.");
        Scale = Config.Bind("HUD", "Scale", 1f, "HUD scale relative to 1080p.");
        AssetRoot = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "assets");
        AddComponent<HudBehaviour>();
        Log.LogInfo("Azeroth HUD loaded; native health, XP, tomes; deferred raw RGBA/IntPtr texture upload. Visual fidelity pending verification.");
    }
}

public sealed class HudBehaviour : MonoBehaviour
{
    public HudBehaviour(IntPtr pointer) : base(pointer) { }
    readonly Dictionary<string, Texture2D> textures = new();
    readonly List<TomeView> tomes = new();
    GUIStyle label, small, title;
    float nextRead;
    int hp, maxHp, level, xp, gold;
    float shield, xpProgress, timer;
    bool active, wasActive, failed;
    Type bridge;
    string[] spellIds = Array.Empty<string>();
    int[] ranks = Array.Empty<int>();
    float[] remaining = Array.Empty<float>(), totals = Array.Empty<float>();
    Texture portrait;
    string heroName="";
    sealed class TomeView
    {
        public Texture Icon; public string Name; public int Level;
        public TomeView(Texture icon,string name,int level){Icon=icon;Name=name;Level=level;}
    }
    static readonly Color Gold = new(0.84f, 0.69f, 0.32f);
    static readonly Color Back = new(0.035f, 0.028f, 0.022f, 0.92f);
    static readonly Dictionary<string,string> Icons = new()
    {
        ["fireball"]="Spell_Fire_FlameBolt", ["frostbolt"]="Spell_Frost_FrostBolt02",
        ["arcane_missiles"]="Spell_Nature_StarFall", ["frost_nova"]="Spell_Frost_FrostNova",
        ["arcane_explosion"]="Spell_Nature_WispSplode", ["blizzard"]="Spell_Frost_IceStorm",
        ["flamestrike"]="Spell_Fire_SelfDestruct", ["fire_blast"]="Spell_Fire_Fireball",
        ["cone_of_cold"]="Spell_Frost_Glacier", ["blast_wave"]="Spell_Fire_SelfDestruct",
        ["pyroblast"]="Spell_Fire_Fireball02", ["ice_barrier"]="Spell_Ice_Lament", ["polymorph"]="Spell_Nature_Polymorph"
    };
    void Update()
    {
        var plugin = Plugin.Current;
        if (plugin == null || !plugin.Enabled.Value) { active=false; return; }
        if (Time.unscaledTime < nextRead) return;
        nextRead = Time.unscaledTime + 0.1f;
        try
        {
            var player = MyPlayer.Instance;
            active = player != null && player.inventory != null && player.hasStarted;
            if (!active) { wasActive=false; return; }
            var inv = player.inventory;
            portrait=inv.characterData.GetIcon(); heroName=inv.characterData.GetName();
            hp=inv.playerHealth.hp; maxHp=inv.playerHealth.maxHp; shield=inv.playerHealth.shield;
            level=inv.playerXp.level; xp=inv.playerXp.GetXpInt(); gold=inv.goldInt;
            xpProgress=XpUtility.CurrentLevelProgress(xp);
            timer=GameManager.Instance != null ? GameManager.Instance.GetAliveTime() : 0;
            tomes.Clear();
            var all=DataManager.Instance?.GetAllTomes();
            if (all != null)
                for (int i=0;i<all.Count;i++)
                {
                    var td=all[i];
                    if(!inv.tomeInventory.HasTome(td.eTome))continue;
                    var rank=inv.tomeInventory.GetTomeLevel(td.eTome);
                    if (rank>0) tomes.Add(new(td.GetIcon(),td.GetName(),rank));
                }
            ReadBridge();
            if (!wasActive)
            {
                plugin.Log.LogInfo($"HUD bound native run: HP {hp}/{maxHp}, level {level}, XP {xp}, tomes {tomes.Count}; available tome slots {InventoryUtility.GetNumAvailableTomeSlots()}");
                wasActive=true;
            }
        }
        catch (Exception e)
        {
            active=false;
            if (!failed) { plugin.Log.LogWarning("HUD read failed: "+e); failed=true; }
        }
    }
    [HideFromIl2Cpp]
    void ReadBridge()
    {
        bridge ??= AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.HudBridge")).FirstOrDefault(t=>t!=null);
        if (bridge==null) return;
        spellIds=ReadStatic(bridge,"SpellIds") as string[] ?? Array.Empty<string>();
        ranks=ReadStatic(bridge,"SpellRanks") as int[] ?? Array.Empty<int>();
        remaining=ReadStatic(bridge,"CooldownsRemaining") as float[] ?? Array.Empty<float>();
        totals=ReadStatic(bridge,"CooldownsTotal") as float[] ?? Array.Empty<float>();
        if(ReadStatic(bridge,"Active") is bool bridgeActive && bridgeActive && ReadStatic(bridge,"ClassId") is string classId && classId.Length>0)
            heroName=char.ToUpperInvariant(classId[0])+classId.Substring(1);
    }
    [HideFromIl2Cpp]
    static object ReadStatic(Type type,string name)=>type.GetProperty(name,BindingFlags.Public|BindingFlags.Static)?.GetValue(null)
        ?? type.GetField(name,BindingFlags.Public|BindingFlags.Static)?.GetValue(null);
    [HideFromIl2Cpp]
    Texture2D Tex(string relative)
    {
        if (textures.TryGetValue(relative,out var texture)) return texture;
        texture=null;
        var original=Path.Combine(Plugin.Current!.AssetRoot,relative.Replace('/',Path.DirectorySeparatorChar));
        var raw=Path.ChangeExtension(original,".rgba");
        var sizeFile=Path.ChangeExtension(original,".size");
        if (File.Exists(raw) && File.Exists(sizeFile))
        {
            // PNG decoding via managed byte[] reaches broken IL2CPP Span constructors in this game.
            // Data is prepared offline, bottom row first, and pinned only for the native upload.
            var size=File.ReadAllText(sizeFile).Split(' ',StringSplitOptions.RemoveEmptyEntries);
            int width=int.Parse(size[0]),height=int.Parse(size[1]);
            byte[] pixels=File.ReadAllBytes(raw);
            if(width<=0 || height<=0 || pixels.Length!=checked(width*height*4))
                throw new InvalidDataException("Invalid HUD RGBA asset: "+raw);
            var tex=new Texture2D(width,height,TextureFormat.RGBA32,false);
            var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
            try { tex.LoadRawTextureData(pinned.AddrOfPinnedObject(),pixels.Length);tex.Apply(false,false); }
            finally { pinned.Free(); }
            texture=tex;
        }
        textures[relative]=texture; return texture;
    }
    [HideFromIl2Cpp]
    static void Solid(Rect r,Color c)
    {
        var old=GUI.color; GUI.color=c; GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=old;
    }
    [HideFromIl2Cpp]
    static void Panel(Rect r)
    {
        Solid(r,Gold); Solid(new(r.x+2,r.y+2,r.width-4,r.height-4),new(0.18f,0.15f,0.10f));
        Solid(new(r.x+4,r.y+4,r.width-8,r.height-8),Back);
    }
    [HideFromIl2Cpp]
    void Text(Rect r,string value,GUIStyle style,Color? color=null)
    {
        var old=GUI.color; GUI.color=Color.black; GUI.Label(new Rect(r.x+1,r.y+1,r.width,r.height),value,style);
        GUI.color=color ?? new Color(0.94f,0.88f,0.70f); GUI.Label(r,value,style);GUI.color=old;
    }
    [HideFromIl2Cpp]
    void Bar(Rect r,float progress,Color color)
    {
        Solid(r,new(0.06f,0.05f,0.04f)); var fill=new Rect(r.x+1,r.y+1,(r.width-2)*Mathf.Clamp01(progress),r.height-2);
        var tex=Tex("Interface/TargetingFrame/UI-StatusBar.png");
        if (tex != null) { var old=GUI.color;GUI.color=color;GUI.DrawTexture(fill,tex);GUI.color=old; } else Solid(fill,color);
    }
    void OnGUI()
    {
        if (!active || Plugin.Current==null) return;
        var previous=GUI.matrix; var oldColor=GUI.color; var oldDepth=GUI.depth;
        try
        {
            label ??=new GUIStyle(GUI.skin.label){fontSize=17,alignment=TextAnchor.MiddleLeft};
            small ??=new GUIStyle(label){fontSize=12};
            title ??=new GUIStyle(label){fontSize=20,alignment=TextAnchor.MiddleCenter};
            var scale=Mathf.Clamp(Screen.height/1080f*Plugin.Current.Scale.Value,0.65f,2f);
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale); GUI.depth=-20;
            float w=Screen.width/scale,h=Screen.height/scale;
            // Original PlayerFrame.xml mirrors UI-TargetingFrame (left=1,right=.09375).
            if(portrait!=null) GUI.DrawTexture(new(35,30,64,64),portrait,ScaleMode.ScaleToFit);
            Solid(new(105,35,152,50),new(0.025f,0.025f,0.025f,0.95f));
            Bar(new(109,59,149,12),maxHp>0?(float)hp/maxHp:0,new(0.08f,0.85f,0.12f));
            // Megabonk has no mana pool: second bar is explicitly XP, never fake mana.
            Bar(new(109,75,149,10),xpProgress,new(0.55f,0.12f,0.74f));
            var frame=Tex("Interface/TargetingFrame/UI-TargetingFrame.png");
            if(frame!=null)GUI.DrawTextureWithTexCoords(new(5,17,278.4f,120),frame,new(1,.21875f,-.90625f,.78125f));
            Text(new(112,31,142,23),heroName,small,Gold);
            Text(new(39,82,28,24),level.ToString(),small,Gold);
            Text(new(113,56,140,17),$"{hp}/{maxHp}"+(shield>0?$" +{shield:0}":""),small,Color.white);
            if(new Rect(109,75,149,10).Contains(Event.current.mousePosition))Text(new(112,86,200,22),$"XP {xp}",small);
            var seconds=Math.Max(0,(int)timer);
            Text(new(w/2-65,6,130,40),$"{seconds/60:00}:{seconds%60:00}",title,Gold);
            Text(new(w-455,7,230,24),$"Gold {gold}",label,Gold);
            // Four stock art quadrants in MainMenuBar.xml, enlarged 1.25x like original capture.
            var menu=Tex("Interface/MainMenuBar/UI-MainMenuBar-Human.png");
            float barLeft=w/2-640;
            for(int q=0;q<4;q++)
                if(menu!=null)GUI.DrawTextureWithTexCoords(new(barLeft+q*320,h-54,320,54),menu,new(0,q*.25f,1,.16796875f));
            Bar(new(barLeft,h-62,1280,8),xpProgress,new(.58f,0,.55f));
            var cap=Tex("Interface/MainMenuBar/UI-MainMenuBar-EndCap-Human.png");
            if(cap!=null)
            {
                GUI.DrawTexture(new(barLeft-138,h-160,160,160),cap);
                GUI.DrawTextureWithTexCoords(new(barLeft+1258,h-160,160,160),cap,new(1,0,-1,1));
            }
            // Native tome icons and levels; no synthetic book inventory or rank values.
            float tx=w/2+414,ty=h-50;
            for(int i=0;i<4;i++)
            {
                var r=new Rect(tx+i*51,ty,47,47);
                var empty=Tex("Interface/Buttons/UI-EmptySlot.png"); if(empty!=null)GUI.DrawTexture(r,empty);
                if(i<tomes.Count)
                {
                    var t=tomes[i]; if(t.Icon!=null)GUI.DrawTexture(new(r.x+5,r.y+5,44,44),t.Icon);
                    Text(new(r.x+4,r.y+34,46,20),t.Level.ToString(),small,Color.white);
                    if(r.Contains(Event.current.mousePosition)) { Panel(new(tx-90,ty-62,335,30));Text(new(tx-82,ty-60,320,24),t.Name,label); }
                }
            }
            int count=Math.Min(12,spellIds.Length);
            {
                float sx=barLeft+8,sy=h-50;
                for(int i=0;i<12;i++)
                {
                    var r=new Rect(sx+i*50,sy,46,46);
                    var empty=Tex("Interface/Buttons/UI-EmptySlot.png");if(empty!=null)GUI.DrawTexture(r,empty);
                    if(i>=count)continue;
                    if(Icons.TryGetValue(spellIds[i],out var name)) {var icon=Tex("Interface/Icons/"+name+".png");if(icon!=null)GUI.DrawTexture(new(r.x+3,r.y+3,44,44),icon);}
                    if(i<remaining.Length&&i<totals.Length&&totals[i]>0&&remaining[i]>0)
                    {
                        Solid(new(r.x,r.y,r.width,r.height*Mathf.Clamp01(remaining[i]/totals[i])),new(0,0,0,0.7f));
                        Text(new(r.x+4,r.y+10,46,26),remaining[i].ToString("0.0"),small,Color.white);
                    }
                    if(i<ranks.Length)Text(new(r.x+30,r.y+30,20,18),ranks[i].ToString(),small,Color.white);
                }
            }
        }
        catch(Exception e)
        {
            if(!failed){Plugin.Current.Log.LogWarning("HUD render failed: "+e);failed=true;}
        }
        finally { GUI.matrix=previous;GUI.color=oldColor;GUI.depth=oldDepth; }
    }
}

