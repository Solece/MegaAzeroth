using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object=UnityEngine.Object;
using NVector3=System.Numerics.Vector3;

namespace AzerothMegabonk.EnemyVisuals;

internal sealed class SourceEnemyCurve
{
    public int Interpolation{get;set;} public int GlobalSequence{get;set;}=-1;
    public int[][] Ranges{get;set;}=Array.Empty<int[]>();public int[] Times{get;set;}=Array.Empty<int>();
    public float[][] Values{get;set;}=Array.Empty<float[]>();
    internal float At(float milliseconds,int sequence,float globalSeconds,int[] globals,float fallback,int component=0)
    {
        if(Times.Length==0||Values.Length==0)return fallback;
        int first=0,last=Math.Min(Times.Length,Values.Length)-1;
        if(GlobalSequence>=0)
        {if(GlobalSequence>=globals.Length)return fallback;milliseconds=globals[GlobalSequence]>0?globalSeconds*1000%globals[GlobalSequence]:0;}
        else if(Ranges.Length>0)
        {if(sequence<0||sequence>=Ranges.Length||Ranges[sequence].Length<2)return fallback;first=Ranges[sequence][0];last=Math.Min(last,Ranges[sequence][1]);}
        if(first<0||first>last||first>=Times.Length)return fallback;
        int at=first;while(at<last&&Times[at+1]<=milliseconds)at++;
        float a=Values[at][Math.Min(component,Values[at].Length-1)];
        if(at==last||Interpolation==0)return a;
        float b=Values[at+1][Math.Min(component,Values[at+1].Length-1)];
        return a+(b-a)*Math.Clamp((milliseconds-Times[at])/(float)Math.Max(1,Times[at+1]-Times[at]),0,1);
    }
}
internal sealed class SourceEnemyTexture
{
    public int Id{get;set;}public string File{get;set;}public int Width{get;set;}public int Height{get;set;}public int Flags{get;set;}
    public string SourcePath{get;set;}public string SourceSha256{get;set;}public string RgbaSha256{get;set;}
    public int AlphaZeroPixels{get;set;}public int AlphaCutoutPixels{get;set;}public int AlphaOpaquePixels{get;set;}
}
internal sealed class SourceEnemyPass
{
    public int SourcePass{get;set;}public int SourceSubmesh{get;set;}public int SourceMaterialIndex{get;set;}
    public int Flags{get;set;}public int Blend{get;set;}public int TextureId{get;set;}public int TextureCoordMode{get;set;}
    public int SourceIndexCount{get;set;}public string SourceCanonicalTriangleSha256{get;set;}
    public int[] Triangles{get;set;}public SourceEnemyCurve Color{get;set;}public SourceEnemyCurve Alpha{get;set;}public SourceEnemyCurve Weight{get;set;}
}
internal sealed class SourceEnemyClip
{
    public int SequenceIndex{get;set;}public int SourceStartMs{get;set;}public int SourceEndMs{get;set;}public int FrameCount{get;set;}
}
internal sealed class SourceEnemyProfile
{
    public string Format{get;set;}public string SourceSha256{get;set;}public int SourceDisplayId{get;set;}public int OriginalVertexCount{get;set;}
    public int[] SourceVertexMap{get;set;}public int[] Globals{get;set;}public SourceEnemyTexture[] Textures{get;set;}public SourceEnemyPass[] Passes{get;set;}
    public int[] EnvironmentVertexIndices{get;set;}public Dictionary<string,SourceEnemyClip> Clips{get;set;}
    public SourceBillboardQuad[] BillboardQuads{get;set;}=Array.Empty<SourceBillboardQuad>();
}
internal sealed class SourceBillboardQuad{public int[] Vertices{get;set;}public int UvDeterminantSign{get;set;}}

internal sealed partial class Model
{
    internal SourceEnemyProfile SourceProfile;
    internal Dictionary<int,Texture2D> SourceTextures;
    internal Material SourceInvisibleMaterial;
    internal Vector2[] SourceUv;
    internal void LoadSourceProfile(string folder,JsonElement json)
    {
        if(!EnemyVarietySelection.DisplayIds.TryGetValue(Key,out int expectedDisplay))return;
        string path=Path.Combine(folder,"source-material-profile.json");
        if(!File.Exists(path))throw new InvalidDataException("Original per-pass enemy material profile missing: "+Key);
        SourceProfile=JsonSerializer.Deserialize<SourceEnemyProfile>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        var p=SourceProfile;var source=json.GetProperty("source");
        if(p.Format!="azeroth-enemy-material-profile-v1"||p.SourceDisplayId!=expectedDisplay||p.SourceDisplayId!=source.GetProperty("displayId").GetInt32()
            ||p.SourceSha256!=source.GetProperty("sha256").GetString()||p.SourceVertexMap.Length!=VertexCount||p.Passes.Length==0
            ||p.SourceVertexMap.Any(v=>v<0||v>=p.OriginalVertexCount))throw new InvalidDataException("Original enemy profile source/vertex mismatch: "+Key);
        if(!p.SourceVertexMap.SequenceEqual(json.GetProperty("sourceVertexMap").EnumerateArray().Select(v=>v.GetInt32())))throw new InvalidDataException("Original enemy vertex map mismatch: "+Key);
        SourceTextures=new();
        foreach(var t in p.Textures)
        {
            if(Path.GetFileName(t.File)!=t.File||t.Width<=0||t.Height<=0)throw new InvalidDataException("Invalid original enemy texture metadata");
            byte[] bytes=File.ReadAllBytes(Path.Combine(folder,t.File));
            if(bytes.Length!=checked(t.Width*t.Height*4)||Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=t.RgbaSha256)throw new InvalidDataException("Original enemy texture hash/size mismatch");
            Texture2D texture;
            if(t.File=="texture.rgba")texture=Texture;
            else
            {
                texture=Retain(new Texture2D(t.Width,t.Height,TextureFormat.RGBA32,false){name="AzerothEnemy_"+Key+"_Texture_"+t.Id});
                var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                try{texture.LoadRawTextureData(pin.AddrOfPinnedObject(),bytes.Length);texture.Apply(false,true);}finally{pin.Free();}
            }
            texture.name="AzerothEnemy_"+Key+"_Texture_"+t.Id;texture.filterMode=FilterMode.Bilinear;
            texture.wrapModeU=(t.Flags&1)!=0?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
            texture.wrapModeV=(t.Flags&2)!=0?TextureWrapMode.Repeat:TextureWrapMode.Clamp;
            SourceTextures.Add(t.Id,texture);
        }
        var shader=Shader.Find("Particles/Standard Unlit");
        if(shader==null)throw new InvalidOperationException("Original enemy material adapter shader unavailable");
        SourceMaterials=new Material[p.Passes.Length];Template.subMeshCount=SourceMaterials.Length;
        for(int i=0;i<p.Passes.Length;i++)
        {
            var pass=p.Passes[i];
            if(pass.SourcePass!=i||pass.Triangles.Length!=pass.SourceIndexCount||pass.Triangles.Length%3!=0
                ||pass.Triangles.Any(v=>v<0||v>=VertexCount)||!SourceTextures.ContainsKey(pass.TextureId)||pass.Blend is not(0 or 1 or 2 or 4)
                ||pass.TextureCoordMode is not(0 or -1)||SourceEnemyGeometry.CanonicalHash(pass.Triangles,p.SourceVertexMap)!=pass.SourceCanonicalTriangleSha256)
                throw new InvalidDataException("Original enemy material geometry mismatch: "+Key+"/"+i);
            Template.SetTriangles(new Il2CppStructArray<int>(pass.Triangles),i,true,0);
            var material=SourceMaterials[i]=Retain(new Material(shader){name="AzerothEnemy_"+Key+"_SourcePass_"+i,mainTexture=SourceTextures[pass.TextureId],color=Color.white});
            bool cutout=pass.Blend==1,transparent=pass.Blend is 2 or 4;
            material.SetInt("_Mode",transparent?2:cutout?1:0);
            material.SetInt("_SrcBlend",(int)(transparent?UnityEngine.Rendering.BlendMode.SrcAlpha:UnityEngine.Rendering.BlendMode.One));
            material.SetInt("_DstBlend",(int)(pass.Blend==4?UnityEngine.Rendering.BlendMode.One:pass.Blend==2?UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha:UnityEngine.Rendering.BlendMode.Zero));
            material.SetInt("_ZWrite",(pass.Flags&16)==0?1:0);
            material.SetInt("_Cull",(int)((pass.Flags&4)!=0?UnityEngine.Rendering.CullMode.Off:UnityEngine.Rendering.CullMode.Back));
            material.SetFloat("_Cutoff",.5f);material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.DisableKeyword("_ALPHAMODULATE_ON");
            if(cutout)material.EnableKeyword("_ALPHATEST_ON");if(transparent)material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue=transparent?3000:cutout?2450:2000;
        }
        if(Material!=null)Object.Destroy(Material);Material=SourceMaterials[0];
        SourceInvisibleMaterial=Retain(new Material(shader){name="AzerothEnemy_"+Key+"_SourceZeroOpacity",mainTexture=Texture,color=Color.clear,renderQueue=3000});
        SourceInvisibleMaterial.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.Zero);SourceInvisibleMaterial.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.One);SourceInvisibleMaterial.SetInt("_ZWrite",0);SourceInvisibleMaterial.EnableKeyword("_ALPHABLEND_ON");
        SourceUv=Template.uv.ToArray();
    }
    internal Color SourceColor(string clip,int frame,int pass,float globalSeconds)
    {
        if(SourceProfile==null)return Color.white;
        if(!SourceProfile.Clips.TryGetValue(clip??"idle",out var c))c=SourceProfile.Clips["idle"];
        float time=c.SourceStartMs+(c.SourceEndMs-c.SourceStartMs)*Math.Clamp(frame/(float)Math.Max(1,c.FrameCount-1),0,1);
        var p=SourceProfile.Passes[pass];var globals=SourceProfile.Globals;
        float r=p.Color?.At(time,c.SequenceIndex,globalSeconds,globals,1,0)??1,g=p.Color?.At(time,c.SequenceIndex,globalSeconds,globals,1,1)??1,b=p.Color?.At(time,c.SequenceIndex,globalSeconds,globals,1,2)??1;
        float alpha=(p.Alpha?.At(time,c.SequenceIndex,globalSeconds,globals,32767)??32767)/32767*(p.Weight?.At(time,c.SequenceIndex,globalSeconds,globals,32767)??32767)/32767;
        return new Color(r,g,b,Math.Clamp(alpha,0,1));
    }
    internal void ApplySourceColors(MeshRenderer renderer,string clip,int frame,float globalSeconds,ref MaterialPropertyBlock[] blocks,ref Material[] chosen)
    {
        if(SourceProfile==null||renderer==null)return;
        if(blocks==null||blocks.Length!=SourceMaterials.Length)blocks=Enumerable.Range(0,SourceMaterials.Length).Select(_=>new MaterialPropertyBlock()).ToArray();
        bool changed=chosen==null;if(chosen==null)chosen=SourceMaterials.ToArray();
        for(int i=0;i<blocks.Length;i++)
        {
            var color=SourceColor(clip,frame,i,globalSeconds);var material=color.a<=0?SourceInvisibleMaterial:SourceMaterials[i];
            if(chosen[i]!=material){chosen[i]=material;changed=true;}
            if(!blocks[i].HasColor("_Color")||blocks[i].GetColor("_Color")!=color){blocks[i].SetColor("_Color",color);renderer.SetPropertyBlock(blocks[i],i);}
        }
        if(changed)renderer.sharedMaterials=chosen;
    }
    internal object SourceMaterialSnapshot(MeshRenderer renderer,Mesh mesh,string clip,int frame,float globalSeconds)
    {
        if(SourceProfile==null)return new{ProfileLoaded=false};
        var assigned=renderer!=null?renderer.sharedMaterials.ToArray():SourceMaterials;
        return new{ProfileLoaded=true,SourceDisplayId=SourceProfile.SourceDisplayId,SourceSha256=SourceProfile.SourceSha256,
            OriginalVertexCount=SourceProfile.OriginalVertexCount,ExportVertexCount=VertexCount,SourceVertexMap=SourceProfile.SourceVertexMap,
            PassCount=SourceProfile.Passes.Length,TextureCount=SourceTextures.Count,LightingPolicy="Source textures/opacity/blends/cull preserved; shipped Unity unlit particle shader lighting adapter",
            ReflectionPolicy="Source texcoord=-1 uses per-instance camera-space sphere reflection UV; derived duplicate vertices map back to original source IDs",
            SourceBillboardQuads=SourceProfile.BillboardQuads.Length,BillboardPolicy="Camera-facing source four-vertex glow/eye quads retain baked weighted centers/shape/UV; Unity adapter, not original client bone matrices",
            MaterialsSource=SourceProfile.Passes.Select((p,i)=>SourceEnemyGeometry.PassSnapshot(this,p,i,assigned.Length>i?assigned[i]:null,mesh,clip,frame,globalSeconds)).ToArray()};
    }
    internal void InspectSourceMaterials(List<string> errors)
    {
        if(SourceProfile==null)return;
        if(SourceMaterials==null||SourceMaterials.Length!=SourceProfile.Passes.Length||Template.subMeshCount!=SourceProfile.Passes.Length)errors.Add("source-profile:partition-count");
        foreach(var pair in SourceTextures)if(pair.Value==null||(pair.Value.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)errors.Add("source-texture:destroyed-or-not-retained:"+pair.Key);
        if(SourceInvisibleMaterial==null||(SourceInvisibleMaterial.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)errors.Add("source-visibility-material:invalid");
        for(int i=0;i<SourceMaterials.Length;i++)
        {var m=SourceMaterials[i];if(m==null||m.shader==null||m.mainTexture!=SourceTextures[SourceProfile.Passes[i].TextureId]||(m.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)errors.Add("source-material:invalid:"+i);}
    }
    internal void ReleaseSourceResources()
    {
        if(SourceTextures!=null)foreach(var texture in SourceTextures.Values)if(texture!=null&&texture!=Texture)Object.Destroy(texture);
        SourceTextures?.Clear();if(SourceInvisibleMaterial!=null)Object.Destroy(SourceInvisibleMaterial);
    }
}

internal static class SourceEnemyGeometry
{
    internal static string CanonicalHash(int[] indices,int[] sourceMap)
    {
        var triples=new (int A,int B,int C)[indices.Length/3];
        for(int i=0;i<triples.Length;i++){int[] t={sourceMap[indices[i*3]],sourceMap[indices[i*3+1]],sourceMap[indices[i*3+2]]};Array.Sort(t);triples[i]=(t[0],t[1],t[2]);}
        Array.Sort(triples);using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        foreach(var t in triples){writer.Write(t.A);writer.Write(t.B);writer.Write(t.C);}
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
    internal static object PassSnapshot(Model model,SourceEnemyPass p,int index,Material actual,Mesh mesh,string clip,int frame,float globalSeconds)
    {
        var color=model.SourceColor(clip,frame,index,globalSeconds);var t=model.SourceProfile.Textures.First(x=>x.Id==p.TextureId);
        int Value(string key)=>actual!=null&&actual.HasProperty(key)?actual.GetInt(key):-1;
        return new{PassIndex=index,SourceSubmesh=p.SourceSubmesh,SourceIndexCount=p.SourceIndexCount,SourceCanonicalTriangleSha256=p.SourceCanonicalTriangleSha256,
            SourceFlags=p.Flags,SourceBlend=p.Blend,SourceTextureId=p.TextureId,SourceTexturePath=t.SourcePath,SourceTextureSha256=t.SourceSha256,
            SourceTextureAlphaZeroPixels=t.AlphaZeroPixels,SourceTextureAlphaCutoutPixels=t.AlphaCutoutPixels,SourceTextureAlphaOpaquePixels=t.AlphaOpaquePixels,
            SourceTextureCoordMode=p.TextureCoordMode,SourceColor=new[]{color.r,color.g,color.b,color.a},VisibilitySuppressed=color.a<=0,
            ActualIndexCount=mesh!=null&&mesh.subMeshCount>index?(int)mesh.GetIndexCount(index):0,ActualMaterial=actual!=null?actual.name:"",ActualShader=actual?.shader?.name,ActualTexture=actual?.mainTexture?.name,
            ActualCull=Value("_Cull"),ActualZWrite=Value("_ZWrite"),ActualSrcBlend=Value("_SrcBlend"),ActualDstBlend=Value("_DstBlend"),ActualAlphaTest=actual!=null&&actual.shaderKeywords.Any(k=>k=="_ALPHATEST_ON"),ActualAlphaBlend=actual!=null&&actual.shaderKeywords.Any(k=>k=="_ALPHABLEND_ON")};
    }
}

// Scorpion's environmental pass needs a per-instance UV channel; source
// four-vertex eye/glow billboards in Zombie/Ent also face the actual camera.
// Append-only duplicate vertices let it coexist with the body's original UVs
// in one renderer while preserving every original source triangle and normal.
internal sealed class SourceEnvironmentInstance
{
    Mesh derived,lastSource;Vector3[] positions,normals,renderPositions;Vector2[] uv;
    internal Mesh Mesh=>derived;
    internal Mesh Apply(Model model,Mesh source,Transform owner)
    {
        var indices=model.SourceProfile?.EnvironmentVertexIndices;var quads=model.SourceProfile?.BillboardQuads;
        if(indices==null||(indices.Length==0&&(quads==null||quads.Length==0)))return source;
        if(derived==null){derived=Model.Retain(Object.Instantiate(model.Template));derived.name="AzerothEnemy_"+model.Key+"_InstanceEnvironmentUV";derived.MarkDynamic();uv=model.SourceUv.ToArray();}
        if(lastSource!=source){positions=source.vertices.ToArray();normals=source.normals.ToArray();derived.vertices=positions;derived.normals=normals;derived.bounds=source.bounds;lastSource=source;}
        var camera=Camera.main;if(camera==null)return derived;
        if(quads.Length>0)
        {
            renderPositions??=new Vector3[positions.Length];Array.Copy(positions,renderPositions,positions.Length);
            var cr=owner.InverseTransformDirection(camera.transform.right);var cu=owner.InverseTransformDirection(camera.transform.up);
            var cameraRight=NVector3.Normalize(new NVector3(cr.x,cr.y,cr.z));var cameraUp=NVector3.Normalize(new NVector3(cu.x,cu.y,cu.z));
            foreach(var q in quads)
            {
                var ids=q.Vertices;NVector3 center=default;
                foreach(int id in ids){var v=positions[id];center+=new NVector3(v.x,v.y,v.z);}center/=4;
                int a=ids[0],b=ids[1],c=ids[2];var av=positions[a];var bv=positions[b];var cv=positions[c];
                var edge1=new NVector3(bv.x-av.x,bv.y-av.y,bv.z-av.z);var edge2=new NVector3(cv.x-av.x,cv.y-av.y,cv.z-av.z);
                float du1=model.SourceUv[b].x-model.SourceUv[a].x,dv1=model.SourceUv[b].y-model.SourceUv[a].y,du2=model.SourceUv[c].x-model.SourceUv[a].x,dv2=model.SourceUv[c].y-model.SourceUv[a].y,det=du1*dv2-dv1*du2;
                if(Math.Abs(det)<1e-9f)continue;
                var right=NVector3.Normalize((edge1*dv2-edge2*dv1)/det);var up=NVector3.Normalize((edge2*du1-edge1*du2)/det);var normalBasis=NVector3.Normalize(NVector3.Cross(right,up));
                var targetRight=q.UvDeterminantSign<0?cameraRight:-cameraRight;var targetNormal=NVector3.Cross(targetRight,cameraUp);
                foreach(int id in ids)
                {var v=positions[id];var offset=new NVector3(v.x,v.y,v.z)-center;var p=center+targetRight*NVector3.Dot(offset,right)+cameraUp*NVector3.Dot(offset,up)+targetNormal*NVector3.Dot(offset,normalBasis);renderPositions[id]=new Vector3(p.X,p.Y,p.Z);}
            }
            derived.vertices=renderPositions;derived.RecalculateBounds();
        }
        var view=camera.worldToCameraMatrix*owner.localToWorldMatrix;var normal=view.inverse.transpose;
        foreach(int i in indices)
        {
            var p=positions[i];float x=view.m00*p.x+view.m01*p.y+view.m02*p.z+view.m03,y=view.m10*p.x+view.m11*p.y+view.m12*p.z+view.m13,z=view.m20*p.x+view.m21*p.y+view.m22*p.z+view.m23;
            var n=normals[i];float nx=normal.m00*n.x+normal.m01*n.y+normal.m02*n.z,ny=normal.m10*n.x+normal.m11*n.y+normal.m12*n.z,nz=normal.m20*n.x+normal.m21*n.y+normal.m22*n.z;
            float il=1/MathF.Sqrt(Math.Max(1e-12f,x*x+y*y+z*z)),nl=1/MathF.Sqrt(Math.Max(1e-12f,nx*nx+ny*ny+nz*nz));x*=il;y*=il;z*=il;nx*=nl;ny*=nl;nz*=nl;
            float dot=x*nx+y*ny+z*nz,rx=x-2*dot*nx,ry=y-2*dot*ny,rz=z-2*dot*nz;
            float denominator=2*MathF.Sqrt(Math.Max(1e-12f,rx*rx+ry*ry+(rz+1)*(rz+1)));uv[i]=new Vector2(rx/denominator+.5f,ry/denominator+.5f);
        }
        derived.uv=uv;return derived;
    }
    internal void Release(){if(derived!=null)Object.Destroy(derived);derived=null;lastSource=null;positions=null;normals=null;renderPositions=null;uv=null;}
}
