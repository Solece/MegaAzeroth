using System.Reflection;
using System.Runtime.InteropServices;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XpUtility = Inventory__Items__Pickups.XpUtility;

namespace AzerothHud;

[BepInPlugin("local.azerothmegabonk.hud", "Azeroth HUD", "0.2.1")]
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
        Log.LogInfo("Azeroth HUD 0.2.1 loaded; native uGUI Canvas/RawImage/TMP using an existing game font; no IMGUI texture or stripped Font factory calls.");
    }
}

public sealed class HudBehaviour : MonoBehaviour
{
    public HudBehaviour(IntPtr pointer) : base(pointer) { }
    readonly Dictionary<string, Texture2D> textures = new();
    readonly List<TomeView> tomes = new();
    GameObject canvasObject;
    RectTransform canvasRect;
    Canvas canvas;
    CanvasScaler scaler;
    TMP_FontAsset font;
    Material fontMaterial;
    readonly List<UiImage> images = new();
    readonly List<UiText> labels = new();
    UiImage portraitImage, hpFill, xpFill, bottomXpFill;
    UiText heroLabel, levelLabel, hpLabel, timerLabel, goldLabel;
    readonly UiImage[] tomeIcons=new UiImage[4], spellIcons=new UiImage[12], cooldownMasks=new UiImage[12];
    readonly UiText[] tomeRanks=new UiText[4], spellRanks=new UiText[12], cooldownLabels=new UiText[12];
    bool uiBuilt;
    sealed class UiImage { public RectTransform Rect; public RawImage Image; public Rect Bounds; public int Anchor; }
    sealed class UiText { public RectTransform Rect; public TextMeshProUGUI Text; public Rect Bounds; public int Anchor; }
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
        if (plugin == null || !plugin.Enabled.Value) { active=false; SetVisible(false); return; }
        if (Time.unscaledTime < nextRead) return;
        nextRead = Time.unscaledTime + 0.1f;
        try
        {
            var player = MyPlayer.Instance;
            active = player != null && player.inventory != null && player.hasStarted;
            if (!active) { wasActive=false; SetVisible(false); return; }
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
            if(!uiBuilt) BuildUi();
            if(!uiBuilt)return;
            SetVisible(true);
            RefreshUi();
            if (!wasActive)
            {
                plugin.Log.LogInfo($"HUD bound native run: HP {hp}/{maxHp}, level {level}, XP {xp}, tomes {tomes.Count}; available tome slots {InventoryUtility.GetNumAvailableTomeSlots()}");
                wasActive=true;
            }
        }
        catch (Exception e)
        {
            active=false;
            SetVisible(false);
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
    void SetVisible(bool visible)
    {
        if(canvasObject!=null && canvasObject.activeSelf!=visible)canvasObject.SetActive(visible);
    }
    [HideFromIl2Cpp]
    RectTransform RectObject(string name)
    {
        var go=new GameObject(name,new[]{Il2CppType.Of<RectTransform>()});
        go.transform.SetParent(canvasRect,false);
        var rect=go.GetComponent<RectTransform>();
        rect.localScale=Vector3.one;
        return rect;
    }
    [HideFromIl2Cpp]
    UiImage Image(string name,Rect bounds,Texture texture,Color color,int anchor=0,Rect? uv=null)
    {
        var rect=RectObject(name);
        var image=rect.gameObject.AddComponent<RawImage>();
        image.raycastTarget=false;image.color=color;image.texture=texture;
        image.uvRect=uv ?? new Rect(0,0,1,1);
        var node=new UiImage{Rect=rect,Image=image,Bounds=bounds,Anchor=anchor};
        images.Add(node);Place(rect,bounds,anchor);
        return node;
    }
    [HideFromIl2Cpp]
    UiText Label(string name,Rect bounds,int size=17,int anchor=0,TextAlignmentOptions alignment=TextAlignmentOptions.MidlineLeft)
    {
        var rect=RectObject(name);
        var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget=false;text.font=font;text.fontSharedMaterial=fontMaterial;text.fontSize=size;
        text.alignment=alignment;
        text.enableAutoSizing=false;text.richText=false;text.color=new Color(.94f,.88f,.70f);text.text="";
        var node=new UiText{Rect=rect,Text=text,Bounds=bounds,Anchor=anchor};
        labels.Add(node);Place(rect,bounds,anchor);return node;
    }
    [HideFromIl2Cpp]
    static void Place(RectTransform rect,Rect bounds,int anchor)
    {
        // Bounds use top-left coordinates. Bottom nodes use a bottom origin with negative y.
        var point=anchor switch {1=>new Vector2(.5f,0),2=>new Vector2(.5f,1),3=>new Vector2(1,1),_=>new Vector2(0,1)};
        rect.anchorMin=point;rect.anchorMax=point;rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=new Vector2(bounds.x,-bounds.y);rect.sizeDelta=new Vector2(bounds.width,bounds.height);
    }
    [HideFromIl2Cpp]
    UiImage Bar(string name,Rect bounds,Color color,int anchor=0)
    {
        Image(name+"Background",bounds,null,new Color(.06f,.05f,.04f),anchor);
        var inner=new Rect(bounds.x+1,bounds.y+1,bounds.width-2,bounds.height-2);
        return Image(name+"Fill",inner,Tex("Interface/TargetingFrame/UI-StatusBar.png"),color,anchor);
    }
    [HideFromIl2Cpp]
    static void Fill(UiImage node,float value)
    {
        var r=node.Bounds;r.width*=Mathf.Clamp01(value);Place(node.Rect,r,node.Anchor);
    }
    [HideFromIl2Cpp]
    void BuildUi()
    {
        // The legacy Font factory is stripped in this build. Borrow the game's loaded TMP font.
        foreach(var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if(candidate==null || candidate.font==null || candidate.fontSharedMaterial==null)continue;
            font=candidate.font;fontMaterial=candidate.fontSharedMaterial;break;
        }
        if(font==null || fontMaterial==null)return;
        // uGUI owns native texture references and draws them without Internal_DrawTextureArguments.
        canvasObject=new GameObject("AzerothHudCanvas",new[]{Il2CppType.Of<RectTransform>()});
        UnityEngine.Object.DontDestroyOnLoad(canvasObject);
        canvasRect=canvasObject.GetComponent<RectTransform>();
        canvas=canvasObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder=100;
        scaler=canvasObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight=1;
        portraitImage=Image("Portrait",new Rect(35,30,64,64),null,Color.white);
        Image("PortraitBack",new Rect(105,35,152,50),null,new Color(.025f,.025f,.025f,.95f));
        hpFill=Bar("Health",new Rect(109,59,149,12),new Color(.08f,.85f,.12f));
        xpFill=Bar("Experience",new Rect(109,75,149,10),new Color(.55f,.12f,.74f));
        Image("PlayerFrame",new Rect(5,17,278.4f,120),Tex("Interface/TargetingFrame/UI-TargetingFrame.png"),Color.white,0,new Rect(1,.21875f,-.90625f,.78125f));
        heroLabel=Label("Hero",new Rect(112,31,142,23),12);heroLabel.Text.color=Gold;
        levelLabel=Label("Level",new Rect(39,82,28,24),12);levelLabel.Text.color=Gold;
        hpLabel=Label("HealthValue",new Rect(113,56,140,17),12);hpLabel.Text.color=Color.white;
        timerLabel=Label("RunTime",new Rect(-65,6,130,40),20,2,TextAlignmentOptions.Center);timerLabel.Text.color=Gold;
        goldLabel=Label("Gold",new Rect(-455,7,230,24),17,3);goldLabel.Text.color=Gold;
        var menu=Tex("Interface/MainMenuBar/UI-MainMenuBar-Human.png");
        for(int q=0;q<4;q++)Image("ActionBar"+q,new Rect(-640+q*320,-54,320,54),menu,Color.white,1,new Rect(0,q*.25f,1,.16796875f));
        bottomXpFill=Bar("BottomExperience",new Rect(-640,-62,1280,8),new Color(.58f,0,.55f),1);
        var cap=Tex("Interface/MainMenuBar/UI-MainMenuBar-EndCap-Human.png");
        Image("LeftGryphon",new Rect(-778,-160,160,160),cap,Color.white,1);
        Image("RightGryphon",new Rect(618,-160,160,160),cap,Color.white,1,new Rect(1,0,-1,1));
        var empty=Tex("Interface/Buttons/UI-EmptySlot.png");
        for(int i=0;i<4;i++)
        {
            float x=414+i*51;
            Image("TomeSlot"+i,new Rect(x,-50,47,47),empty,Color.white,1);
            tomeIcons[i]=Image("TomeIcon"+i,new Rect(x+4,-46,39,39),null,Color.white,1);
            tomeRanks[i]=Label("TomeRank"+i,new Rect(x+4,-16,46,20),12,1);tomeRanks[i].Text.color=Color.white;
        }
        for(int i=0;i<12;i++)
        {
            float x=-632+i*50;
            Image("SpellSlot"+i,new Rect(x,-50,46,46),empty,Color.white,1);
            spellIcons[i]=Image("SpellIcon"+i,new Rect(x+3,-47,40,40),null,Color.white,1);
            cooldownMasks[i]=Image("SpellCooldownMask"+i,new Rect(x,-50,46,46),null,new Color(0,0,0,.7f),1);
            cooldownLabels[i]=Label("SpellCooldown"+i,new Rect(x+4,-40,46,26),12,1);cooldownLabels[i].Text.color=Color.white;
            spellRanks[i]=Label("SpellRank"+i,new Rect(x+30,-20,20,18),12,1);spellRanks[i].Text.color=Color.white;
        }
        uiBuilt=true;
        Plugin.Current.Log.LogInfo($"HUD native Canvas constructed: {images.Count} RawImages, {labels.Count} TMP labels; borrowed existing game TMP font/material; IMGUI disabled.");
    }
    [HideFromIl2Cpp]
    void RefreshUi()
    {
        float scale=Mathf.Clamp(Plugin.Current.Scale.Value,.5f,2);
        scaler.referenceResolution=new Vector2(1920/scale,1080/scale);
        portraitImage.Image.texture=portrait;portraitImage.Image.enabled=portrait!=null;
        heroLabel.Text.text=heroName.Equals("Mage",StringComparison.OrdinalIgnoreCase)?"Маг":heroName;
        levelLabel.Text.text=level.ToString();
        hpLabel.Text.text=$"{hp}/{maxHp}"+(shield>0?$" +{shield:0}":"");
        var seconds=Math.Max(0,(int)timer);timerLabel.Text.text=$"{seconds/60:00}:{seconds%60:00}";
        goldLabel.Text.text=$"Золото {gold}";
        Fill(hpFill,maxHp>0?(float)hp/maxHp:0);Fill(xpFill,xpProgress);Fill(bottomXpFill,xpProgress);
        for(int i=0;i<4;i++)
        {
            bool present=i<tomes.Count;
            tomeIcons[i].Image.enabled=present;
            tomeIcons[i].Image.texture=present?tomes[i].Icon:null;
            tomeRanks[i].Text.text=present?tomes[i].Level.ToString():"";
        }
        for(int i=0;i<12;i++)
        {
            bool present=i<spellIds.Length;
            Texture2D icon=null;
            if(present && Icons.TryGetValue(spellIds[i],out var name))icon=Tex("Interface/Icons/"+name+".png");
            spellIcons[i].Image.texture=icon;spellIcons[i].Image.enabled=icon!=null;
            bool cooling=present&&i<remaining.Length&&i<totals.Length&&totals[i]>0&&remaining[i]>0;
            cooldownMasks[i].Image.enabled=cooling;
            var bounds=cooldownMasks[i].Bounds;
            bounds.height*=cooling?Mathf.Clamp01(remaining[i]/totals[i]):0;
            Place(cooldownMasks[i].Rect,bounds,1);
            cooldownLabels[i].Text.text=cooling?remaining[i].ToString("0.0"):"";
            spellRanks[i].Text.text=present&&i<ranks.Length?ranks[i].ToString():"";
        }
    }
    void OnDestroy()
    {
        if(canvasObject!=null)UnityEngine.Object.Destroy(canvasObject);
        foreach(var texture in textures.Values)if(texture!=null)UnityEngine.Object.Destroy(texture);
        // Font and shared material belong to the game; never destroy the borrowed assets.
        font=null;fontMaterial=null;
        textures.Clear();images.Clear();labels.Clear();
    }
}
