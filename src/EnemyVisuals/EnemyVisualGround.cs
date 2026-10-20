using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using UnityEngine;

namespace AzerothMegabonk.EnemyVisuals;

internal static class EnemyVisualGround
{
    internal static int GroundLayerMask=>GameManager.Instance!=null?GameManager.Instance.whatIsGroundAndObjects.value:0;
    internal static Vector3 NativeFeet(Enemy enemy,out float groundCheckY)
    {
        var check=enemy.GetGroundCheckPosition();groundCheckY=check.y;
        var collider=enemy.collider;
        if(collider!=null && collider.enabled)
        {
            var b=collider.bounds;
            if(float.IsFinite(b.min.y) && b.size.y>.001f)return new Vector3(b.center.x,b.min.y,b.center.z);
        }
        return float.IsFinite(check.y)?check:enemy.transform.position;
    }
    static bool Actor(Collider collider)
    {
        // Terrain can be procedural and have isStatic=false; no attached rigidbody
        // plus no actor component in ancestry identifies a static collision surface.
        if(collider==null || collider.isTrigger || collider.attachedRigidbody!=null)return true;
        for(var t=collider.transform;t!=null;t=t.parent)
            if(t.GetComponent<Enemy>()!=null || t.GetComponent<MyPlayer>()!=null || t.GetComponent<PlayerMovement>()!=null)return true;
        return false;
    }
    internal static bool TryGround(Vector3 nativeFeet,out float groundY)
    {
        groundY=nativeFeet.y;float nearest=float.PositiveInfinity;bool found=false;
        int mask=GroundLayerMask;if(mask==0)return false;
        var hits=Physics.RaycastAll(nativeFeet+Vector3.up*2.01f,Vector3.down,4.02f,mask,QueryTriggerInteraction.Ignore);
        foreach(var hit in hits)
        {
            if(hit.normal.y<=.45f || Actor(hit.collider) || !float.IsFinite(hit.point.y))continue;
            float delta=Math.Abs(hit.point.y-nativeFeet.y);if(delta>2f || delta>=nearest)continue;
            nearest=delta;groundY=hit.point.y;found=true;
        }
        return found;
    }
    internal static bool CameraVisible(Camera camera,Renderer renderer,out Vector3 centerViewport)
    {
        centerViewport=Vector3.zero;
        if(camera==null || renderer==null)return false;
        var b=renderer.bounds;centerViewport=camera.WorldToViewportPoint(b.center);
        if(!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff || (camera.cullingMask&(1<<renderer.gameObject.layer))==0)return false;
        float minX=float.PositiveInfinity,minY=float.PositiveInfinity,maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;bool inFront=false;
        for(int i=0;i<8;i++)
        {
            var p=camera.WorldToViewportPoint(b.center+new Vector3((i&1)==0?-b.extents.x:b.extents.x,(i&2)==0?-b.extents.y:b.extents.y,(i&4)==0?-b.extents.z:b.extents.z));
            if(p.z<=0)continue;inFront=true;minX=Math.Min(minX,p.x);maxX=Math.Max(maxX,p.x);minY=Math.Min(minY,p.y);maxY=Math.Max(maxY,p.y);
        }
        return inFront && maxX>=0 && minX<=1 && maxY>=0 && minY<=1;
    }
}
