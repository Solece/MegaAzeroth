using System.Reflection;
using HarmonyLib;
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

[BepInPlugin("local.azerothmegabonk.hud", "Azeroth HUD", "0.8.1")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Current;
    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<float> Scale = null!;
    internal string AssetRoot = "";
    Harmony harmony;
    public override bool Unload(){harmony?.UnpatchSelf();HudBehaviour.Current?.DisposeVisuals();return true;}
    public override void Load()
    {
        Current = this;
        Enabled = Config.Bind("HUD", "Enabled", true, "Show the Azeroth-style gameplay overlay. Native encounter windows remain available.");
        Scale = Config.Bind("HUD", "Scale", 1f, "HUD scale relative to 1080p.");
        AssetRoot = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "assets");
        harmony=new Harmony("local.azerothmegabonk.hud");harmony.PatchAll(typeof(Plugin).Assembly);
        AddComponent<HudBehaviour>();
        Log.LogInfo("Azeroth HUD 0.8.1 loaded for all source classes; native gold/timer retained, native XP strip decorations masked, source player frame below native top row.");
    }
}

public sealed class HudBehaviour : MonoBehaviour
{
    public HudBehaviour(IntPtr pointer) : base(pointer) { Current=this; }
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
    UiImage portraitImage, hpFill, shieldFill, xpFill;
    UiText heroLabel, levelLabel, hpLabel, shieldLabel, xpLabel, spellsTitle;
    int playerImageCount,playerLabelCount;
    UiImage playerFrame;
    readonly UiImage[] tomeIcons=new UiImage[4], spellFrames=new UiImage[16], spellIcons=new UiImage[16], cooldownMasks=new UiImage[16];
    readonly UiText[] tomeRanks=new UiText[4], spellRanks=new UiText[16], cooldownLabels=new UiText[16];
    readonly NativeHudMask nativeHud=new();
    readonly NativeItemLayout itemLayout=new();
    readonly PauseSpellInventory pauseInventory=new();
    readonly EncounterSpellInventory encounterInventory=new();
    readonly PostCombatAdapter postCombat=new();
    internal static HudBehaviour Current;
    bool uiBuilt;
    sealed class UiImage { public RectTransform Rect; public RawImage Image; public Rect Bounds; public int Anchor; }
    sealed class UiText { public RectTransform Rect; public TextMeshProUGUI Text; public Rect Bounds; public int Anchor; }
    float nextRead;
    int hp, maxHp, level, xp, xpInLevel, xpLevelMax;
    float shield, xpProgress;
    bool active, wasActive, failed;
    Type bridge, portraitBridge;
    string[] spellIds = Array.Empty<string>();
    string[] spellNames=Array.Empty<string>(), spellDescriptions=Array.Empty<string>(), spellIconStems=Array.Empty<string>();
    int[] ranks = Array.Empty<int>();
    float[] remaining = Array.Empty<float>(), totals = Array.Empty<float>();
    Texture portrait;
    Texture sourcePortrait;
    string heroName="";
    bool sourceHeroActive;
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
        ["cone_of_cold"]="Spell_Frost_Glacier", ["blast_wave"]="Spell_Holy_Excorcism_02",
        ["pyroblast"]="Spell_Fire_Fireball02", ["ice_barrier"]="Spell_Ice_Lament", ["polymorph"]="Spell_Nature_Polymorph"
    };
    void Update()
    {
        var plugin = Plugin.Current;
        if (plugin == null || !plugin.Enabled.Value) { active=false; SetVisible(false); pauseInventory.Restore(); encounterInventory.Restore(); return; }
        if (Time.unscaledTime < nextRead) return;
        nextRead = Time.unscaledTime + 0.1f;
        try
        {
            var player = MyPlayer.Instance;
            active = player != null && player.inventory != null && player.hasStarted;
            if(PostCombatAdapter.IsPostCombat())
            {
                ReadBridge();if(!uiBuilt)BuildUi();
                SetVisible(false);pauseInventory.Restore();encounterInventory.Restore();
                HudDiagnostics.PostCombat=true;
                if(uiBuilt)postCombat.Refresh(sourceHeroActive,font,fontMaterial,sourcePortrait,spellIds,ranks,spellNames,spellDescriptions,spellIconStems,Tex);
                return;
            }
            HudDiagnostics.PostCombat=false;postCombat.Restore();
            if (!active) { wasActive=false; SetVisible(false); pauseInventory.Restore(); encounterInventory.Restore(); return; }
            bool paused=UiManager.Instance?.pause!=null && UiManager.Instance.pause.IsPaused();
            var inv = player.inventory;
            portrait=inv.characterData.GetIcon(); heroName=inv.characterData.GetName();
            hp=inv.playerHealth.hp; maxHp=inv.playerHealth.maxHp; shield=inv.playerHealth.shield;
            level=inv.playerXp.level; xp=inv.playerXp.GetXpInt();
            xpProgress=XpUtility.CurrentLevelProgress(xp);
            xpInLevel=Math.Max(0,xp-XpUtility.XpTotalCurrentLevel(xp));
            xpLevelMax=Math.Max(1,XpUtility.XpTotalNextLevel(xp)-XpUtility.XpTotalCurrentLevel(xp));
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
            if(sourceHeroActive)postCombat.RememberPortrait(sourcePortrait);
            PostCombatAdapter.RefreshSources(spellIds,ranks,spellNames,spellDescriptions,spellIconStems,Tex);
            if(!uiBuilt) BuildUi();
            if(!uiBuilt)return;
            encounterInventory.Refresh(sourceHeroActive,font,fontMaterial,spellIds,ranks,spellNames,spellDescriptions,spellIconStems,Tex);
            pauseInventory.Refresh(paused, sourceHeroActive, font, fontMaterial, spellIds, ranks, spellNames, spellDescriptions, spellIconStems, Tex);
            HudDiagnostics.Paused=paused;
            HudDiagnostics.NativeXp=xp;HudDiagnostics.Level=level;HudDiagnostics.XpInLevel=xpInLevel;HudDiagnostics.XpLevelMaximum=xpLevelMax;HudDiagnostics.XpProgress=xpProgress;
            if(paused){SetVisible(false);return;}
            SetVisible(true);
            RefreshUi();
            nativeHud.Refresh();
            itemLayout.Refresh(176f*Screen.height/1080f*Mathf.Clamp(Plugin.Current.Scale.Value,.5f,2));
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
            pauseInventory.Restore();
            if (!failed) { plugin.Log.LogWarning("HUD read failed: "+e); failed=true; }
        }
    }
    [HideFromIl2Cpp]
    void ReadBridge()
    {
        sourceHeroActive=false;
        sourcePortrait=null;
        bridge ??= AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.HudBridge")).FirstOrDefault(t=>t!=null);
        if (bridge==null) return;
        spellIds=ReadStatic(bridge,"SpellIds") as string[] ?? Array.Empty<string>();
        ranks=ReadStatic(bridge,"SpellRanks") as int[] ?? Array.Empty<int>();
        spellNames=ReadStatic(bridge,"SpellNames") as string[] ?? Array.Empty<string>();
        spellDescriptions=ReadStatic(bridge,"SpellDescriptions") as string[] ?? Array.Empty<string>();
        spellIconStems=ReadStatic(bridge,"SpellIcons") as string[] ?? Array.Empty<string>();
        remaining=ReadStatic(bridge,"CooldownsRemaining") as float[] ?? Array.Empty<float>();
        totals=ReadStatic(bridge,"CooldownsTotal") as float[] ?? Array.Empty<float>();
        if(ReadStatic(bridge,"Active") is bool bridgeActive && bridgeActive && ReadStatic(bridge,"ClassId") is string classId && classId.Length>0)
        {
            SourcePresentation.ClassId=classId;
            heroName=SourcePresentation.ClassName;
            sourceHeroActive=SourcePresentation.IsSourceClass;
            // Azeroth Ice Barrier has its own pool; it is additional to the native player shield.
            if(sourceHeroActive && ReadStatic(bridge,"BarrierRemaining") is float barrier)
                shield+=Math.Max(0,barrier);
            if(sourceHeroActive)
            {
                portraitBridge ??= AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.MageVisualBridge")).FirstOrDefault(t=>t!=null);
                if(portraitBridge!=null && SourcePresentation.PortraitReady && ReadStatic(portraitBridge,"PortraitTexture") is Texture head && head!=null)
                    sourcePortrait=head;
            }
        }
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
            tex.hideFlags=HideFlags.DontUnloadUnusedAsset;
            tex.name=Path.GetFileNameWithoutExtension(original);
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
        if(!visible){nativeHud.Restore(Plugin.Current?.Enabled.Value==true && sourceHeroActive && MyPlayer.Instance?.inventory!=null);itemLayout.Restore();}
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
    static Rect PlayerRect(float x,float y,float width,float height)
    {
        // One coordinate system for the 232x100 cropped/mirrored source texture and XML widgets.
        const float scale=1.6f,originX=-29,originY=0;
        return new Rect(originX+x*scale,originY+y*scale,width*scale,height*scale);
    }
    [HideFromIl2Cpp]
    void BuildUi()
    {
        // The legacy Font factory is stripped in this build. Borrow the game's loaded TMP font.
        foreach(var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
        {
            if(candidate==null || candidate.font==null || candidate.fontSharedMaterial==null)continue;
            font=candidate.font;
            fontMaterial=new Material(candidate.fontSharedMaterial);
            fontMaterial.name="AzerothHudReadableText";
            if(fontMaterial.HasProperty("_OutlineWidth"))fontMaterial.SetFloat("_OutlineWidth",0);
            if(fontMaterial.HasProperty("_OutlineSoftness"))fontMaterial.SetFloat("_OutlineSoftness",0);
            if(fontMaterial.HasProperty("_FaceDilate"))fontMaterial.SetFloat("_FaceDilate",0);
            fontMaterial.DisableKeyword("UNDERLAY_ON");fontMaterial.DisableKeyword("UNDERLAY_INNER");
            break;
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
        // Source reference: reports/reference/azeroth-original-hud.png and extracted PlayerFrame.xml.
        // HP source rect=(106,41,119,12), second row=(106,52,119,12), portrait=(42,12,64,64).
        // A native mana resource does not exist in this hybrid. The second row is explicitly shield.
        Image("PlayerBarsBackground",PlayerRect(106,22,119,42),null,new Color(.018f,.018f,.018f,.95f));
        // Temporary original Mage spell artwork fits entirely inside the source portrait ring.
        // Replace with an actual Mage portrait when that separate asset becomes available.
        portraitImage=Image("Portrait",PlayerRect(52,22,44,44),null,Color.white);
        hpFill=Bar("Health",PlayerRect(106,41,119,12),new Color(.04f,.82f,.07f));
        shieldFill=Bar("Shield",PlayerRect(106,52,119,12),new Color(.05f,.48f,.92f));
        playerFrame=Image("PlayerFrame",PlayerRect(0,0,232,100),Tex("Interface/TargetingFrame/UI-TargetingFrame.png"),Color.white,0,new Rect(1,.21875f,-.90625f,.78125f));
        heroLabel=Label("Hero",PlayerRect(106,21,119,20),22,0,TextAlignmentOptions.Center);heroLabel.Text.color=Gold;
        levelLabel=Label("Level",PlayerRect(43,59,20,16),18,0,TextAlignmentOptions.Center);levelLabel.Text.color=Gold;
        hpLabel=Label("HealthValue",PlayerRect(107,41,117,12),20,0,TextAlignmentOptions.Center);hpLabel.Text.color=Color.white;
        shieldLabel=Label("ShieldValue",PlayerRect(107,52,117,12),16,0,TextAlignmentOptions.Center);shieldLabel.Text.color=new Color(.8f,.94f,1);
        playerImageCount=images.Count;playerLabelCount=labels.Count;
        // Source MainMenuBar.xml: 1024x13 XP bar above the action bar, purple (.58,0,.55),
        // four original atlas overlays (each 256x10) at y=3. No invented HP-adjacent XP widget.
        const float xpArtScale=1300f/1024f;
        xpFill=Bar("Experience",new Rect(-650,-116-13*xpArtScale,1300,13*xpArtScale),new Color(.58f,0,.55f),1);
        var xpAtlas=Tex("Interface/MainMenuBar/UI-MainMenuBar-Human.png");
        for(int quadrant=0;quadrant<4;quadrant++)
            Image("ExperienceSourceFrame"+quadrant,new Rect(-650+quadrant*325,-116-10*xpArtScale,325,10*xpArtScale),xpAtlas,Color.white,1,new Rect(0,.16796875f+quadrant*.25f,1,.0390625f));
        xpLabel=Label("ExperienceValue",new Rect(-640,-116-13*xpArtScale,1280,13*xpArtScale),13,1,TextAlignmentOptions.Center);xpLabel.Text.color=Color.white;
        HudDiagnostics.ExperienceSource="MainMenuBar.xml 1024x13 + four 256x10 original atlas quadrants; native XP thresholds";
        // Reuse only the source atlas's blank stone strip and silver rails. The other
        // strips contain baked empty slots and must not be stretched behind live cells.
        var plate=Tex("Interface/MainMenuBar/UI-MainMenuBar-Human.png");
        Image("ActionBarBackground",new Rect(-650,-116,1300,108),plate,Color.white,1,new Rect(35f/256,128f/256,220f/256,38f/256));
        Image("ActionBarTopRail",new Rect(-650,-116,1300,12),plate,Color.white,1,new Rect(0,168f/256,1,12f/256));
        Image("ActionBarBottomRail",new Rect(-650,-20,1300,12),plate,Color.white,1,new Rect(0,168f/256,1,12f/256));
        var cap=Tex("Interface/MainMenuBar/UI-MainMenuBar-EndCap-Human.png");
        Image("LeftGryphon",new Rect(-778,-168,160,160),cap,Color.white,1);
        Image("RightGryphon",new Rect(618,-168,160,160),cap,Color.white,1,new Rect(1,0,-1,1));
        var empty=Tex("Interface/Buttons/UI-EmptySlot.png");
        var booksTitle=Label("BooksTitle",new Rect(389,-102,250,26),22,1);booksTitle.Text.text="Книжки";booksTitle.Text.color=Gold;
        spellsTitle=Label("SourceAbilitiesTitle",new Rect(-619,-102,800,26),22,1);spellsTitle.Text.text="Способности мага";spellsTitle.Text.color=Gold;
        for(int i=0;i<4;i++)
        {
            float x=384+i*61;
            Image("TomeSlot"+i,new Rect(x,-72,56,56),empty,Color.white,1);
            tomeIcons[i]=Image("TomeIcon"+i,new Rect(x+5,-67,46,46),null,Color.white,1);
            tomeRanks[i]=Label("TomeRank"+i,new Rect(x+4,-38,49,22),18,1,TextAlignmentOptions.Right);tomeRanks[i].Text.color=Color.white;
        }
        for(int i=0;i<16;i++)
        {
            float x=-620+i*56;
            spellFrames[i]=Image("SpellSlot"+i,new Rect(x,-72,56,56),empty,Color.white,1);
            spellIcons[i]=Image("SpellIcon"+i,new Rect(x+5,-67,46,46),null,Color.white,1);
            cooldownMasks[i]=Image("SpellCooldownMask"+i,new Rect(x,-72,56,56),null,new Color(0,0,0,.7f),1);
            cooldownLabels[i]=Label("SpellCooldown"+i,new Rect(x+3,-60,50,30),18,1,TextAlignmentOptions.Center);cooldownLabels[i].Text.color=Color.white;
            spellRanks[i]=Label("SpellRank"+i,new Rect(x+34,-38,20,20),17,1);spellRanks[i].Text.color=Color.white;
        }
        uiBuilt=true;
        Plugin.Current.Log.LogInfo($"HUD native Canvas constructed: {images.Count} RawImages, {labels.Count} TMP labels; borrowed game TMP font, private material without thick outline; IMGUI disabled.");
    }
    [HideFromIl2Cpp]
    void RefreshUi()
    {
        float scale=Mathf.Clamp(Plugin.Current.Scale.Value,.5f,2);
        scaler.referenceResolution=new Vector2(1920/scale,1080/scale);
        var visiblePortrait=sourceHeroActive?(sourcePortrait??SourcePresentation.StaticPortrait??portrait):portrait;
        portraitImage.Image.texture=visiblePortrait;portraitImage.Image.enabled=visiblePortrait!=null;
        var portraitBounds=sourceHeroActive&&sourcePortrait!=null?PlayerRect(42,12,64,64):PlayerRect(52,22,44,44);
        portraitImage.Bounds=portraitBounds;
        heroLabel.Text.text=heroName;
        spellsTitle.Text.text="Способности: "+heroName;
        levelLabel.Text.text=level.ToString();
        hpLabel.Text.text=$"{hp} / {maxHp}";
        shieldLabel.Text.text=shield>0?$"Щит {shield:0}":"Щит —";
        xpLabel.Text.text=$"Опыт: {xpInLevel} / {xpLevelMax}";
        Fill(hpFill,maxHp>0?(float)hp/maxHp:0);
        Fill(shieldFill,maxHp>0?shield/maxHp:0);
        Fill(xpFill,xpProgress);
        PlacePlayerCluster(scale);
        for(int i=0;i<4;i++)
        {
            bool present=i<tomes.Count;
            tomeIcons[i].Image.enabled=present;
            tomeIcons[i].Image.texture=present?tomes[i].Icon:null;
            tomeRanks[i].Text.text=present?tomes[i].Level.ToString():"";
        }
        for(int i=0;i<16;i++)
        {
            bool present=i<spellIds.Length;
            spellFrames[i].Image.enabled=present;
            Texture2D icon=null;
            if(present)
            {
                string name=i<spellIconStems.Length?spellIconStems[i]:null;
                if(string.IsNullOrWhiteSpace(name))Icons.TryGetValue(spellIds[i],out name);
                if(!string.IsNullOrWhiteSpace(name))icon=Tex("Interface/Icons/"+name+".png");
            }
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
    [HideFromIl2Cpp]
    void PlacePlayerCluster(float scale)
    {
        float nativeBottom=0;
        foreach(var timer in Resources.FindObjectsOfTypeAll<GameTimer>())
        {
            if(timer==null || !timer.gameObject.scene.IsValid())continue;
            nativeBottom=Mathf.Max(nativeBottom,TopRowBottom(timer.t_timerRun),TopRowBottom(timer.t_timerStage),TopRowBottom(timer.t_timerSpeedrun));
        }
        foreach(var counter in Resources.FindObjectsOfTypeAll<KillsAndGoldCounter>())
        {
            if(counter==null || !counter.gameObject.scene.IsValid())continue;
            nativeBottom=Mathf.Max(nativeBottom,TopRowBottom(counter.t_gold),TopRowBottom(counter.t_kills),TopRowBottom(counter.t_silver),TopRowBottom(counter.t_chestPrice));
        }
        float top=Mathf.Max(80,Screen.height-Screen.safeArea.yMax+12,nativeBottom+12);
        float unitToPixels=Mathf.Max(.0001f,Screen.height/1080f*scale);
        float offset=top/unitToPixels;
        for(int i=0;i<playerImageCount;i++)
        {
            var node=images[i];var bounds=node.Bounds;bounds.y+=offset;
            if(node==hpFill)bounds.width*=maxHp>0?Mathf.Clamp01((float)hp/maxHp):0;
            if(node==shieldFill)bounds.width*=maxHp>0?Mathf.Clamp01(shield/maxHp):0;
            Place(node.Rect,bounds,node.Anchor);
        }
        for(int i=0;i<playerLabelCount;i++){var node=labels[i];var bounds=node.Bounds;bounds.y+=offset;Place(node.Rect,bounds,node.Anchor);}
        HudDiagnostics.PlayerClusterTopScreenPixels=top;
        HudDiagnostics.NativeTopRowBottomScreenPixels=nativeBottom;
        HudDiagnostics.ScreenSafeArea=Screen.safeArea.ToString();
        HudDiagnostics.PlayerClusterScreenBounds=NativeItemLayout.ScreenBounds(playerFrame.Rect,NativeItemLayout.FindCanvasCamera(playerFrame.Rect)).ToString();
    }
    [HideFromIl2Cpp]
    static float TopRowBottom(Graphic graphic)
    {
        if(graphic==null || !graphic.enabled || !graphic.gameObject.activeInHierarchy)return 0;
        var rect=NativeItemLayout.ScreenBounds(graphic.rectTransform,NativeItemLayout.FindCanvasCamera(graphic.transform));
        float top=Screen.height-rect.yMax,bottom=Screen.height-rect.yMin;
        return rect.width>0 && rect.height>0 && top>=-8 && bottom<Screen.height*.25f?Mathf.Max(0,bottom):0;
    }
    void LateUpdate()
    {
        pauseInventory.Enforce();
        encounterInventory.Enforce();
        postCombat.Enforce();
        if(Plugin.Current?.Enabled.Value==true && sourceHeroActive && MyPlayer.Instance?.inventory!=null)nativeHud.EnforceXpOnly();
        if(active&&uiBuilt&&canvasObject!=null&&canvasObject.activeSelf)
        {
            nativeHud.Enforce();
            itemLayout.Refresh(176f*Screen.height/1080f*Mathf.Clamp(Plugin.Current.Scale.Value,.5f,2));
        }
    }
    [HideFromIl2Cpp]
    internal void ForceRefresh(){nextRead=0;Update();Canvas.ForceUpdateCanvases();}
    [HideFromIl2Cpp]
    internal void ProbeScroll(float value){if(HudDiagnostics.PostCombat)postCombat.SetScroll(value);else pauseInventory.SetScroll(value);}
    [HideFromIl2Cpp]
    internal bool ProbeTooltip(int index)=>pauseInventory.ShowTooltip(index);
    [HideFromIl2Cpp]
    internal string[] DescribePause()=>pauseInventory.Describe();
    [HideFromIl2Cpp]
    internal string[] DescribeResults()=>postCombat.DescribeInventory();
    void OnDestroy()=>DisposeVisuals();
    [HideFromIl2Cpp]
    internal void DisposeVisuals()
    {
        nativeHud.Restore();itemLayout.Restore();pauseInventory.Dispose();postCombat.Dispose();encounterInventory.Dispose();
        if(Current==this)Current=null;
        if(canvasObject!=null)UnityEngine.Object.Destroy(canvasObject);
        foreach(var texture in textures.Values)if(texture!=null)UnityEngine.Object.Destroy(texture);
        // The font belongs to the game; only the private readable material belongs to this HUD.
        if(fontMaterial!=null)UnityEngine.Object.Destroy(fontMaterial);
        font=null;fontMaterial=null;
        textures.Clear();images.Clear();labels.Clear();
    }
}
