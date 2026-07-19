# Kayseri / Ore Empire - procedural low-poly asset builder for Blender 5.2
# Run inside Blender. Builds assets from Docs/ASSETS.md and exports .glb to Unity.
import bpy, bmesh, math, os, random, traceback
from mathutils import Vector

EXPORT_DIR = r"C:\Users\oquzt\Documents\GitHub\project_kayseri\Assets\Art\Models"
REPORT = r"C:\Users\oquzt\AppData\Local\Temp\claude\C--Users-oquzt-Desktop-BLENDER\09d1028d-9ee8-4f07-9c8b-7897aecb4f6f\scratchpad\report.txt"
os.makedirs(EXPORT_DIR, exist_ok=True)

# ---------------- palette ----------------
PAL = {
 'grass':'#86D06A','grass_dk':'#4E9440','dirt':'#D8B888','wood':'#8A5A3C','snow':'#F2F7FF',
 'rock':'#8B8FA3','rock_dk':'#565E6B','steel':'#7A879F','gold_warm':'#F2C14E','gold_warm_dk':'#C6922E',
 'orange':'#F5923E','red_roof':'#E8663B','train_green':'#2FA96B','truck_blue':'#3E7CC4',
 'coal':'#2B2F36','copper':'#E08A4C','iron':'#9AA0AA','silver':'#D7DCE5','gold':'#FFCF4D',
 'ruby':'#E5484D','emerald':'#2FBF71','sapphire':'#3B82F6','diamond':'#7FE3F0',
 'coin':'#FFD34E','gem':'#A855F7','dark':'#20242B','concrete':'#B9BEC7','gravel':'#9AA0AA',
 'glass':'#Bfe8f5','cyan_water':'#7FD4E8',
}

def _s2l(c):
    c=c/255.0
    return c/12.92 if c<=0.04045 else ((c+0.055)/1.055)**2.4
def hexcol(h):
    h=h.lstrip('#'); r,g,b=(int(h[i:i+2],16) for i in (0,2,4))
    return (_s2l(r),_s2l(g),_s2l(b),1.0)

_mats={}
def mat(key, metallic=0.0, rough=0.65, hexoverride=None):
    name = 'M_'+key
    if name in _mats: return _mats[name]
    m=bpy.data.materials.new(name); m.use_nodes=True
    b=m.node_tree.nodes.get('Principled BSDF')
    col=hexcol(hexoverride or PAL[key])
    b.inputs['Base Color'].default_value=col
    b.inputs['Roughness'].default_value=rough
    b.inputs['Metallic'].default_value=metallic
    try: b.inputs['Specular IOR Level'].default_value=0.2
    except: pass
    m.diffuse_color=col
    _mats[name]=m
    return m

# ---------------- primitives ----------------
def _link(me,name):
    ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob); return ob
def setmat(ob,m):
    ob.data.materials.clear(); ob.data.materials.append(m)

def box(sx,sy,sz,loc=(0,0,0),m=None,name='box'):
    bm=bmesh.new(); bmesh.ops.create_cube(bm,size=1.0)
    for v in bm.verts: v.co.x*=sx; v.co.y*=sy; v.co.z*=sz
    me=bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob=_link(me,name); ob.location=loc
    if m: setmat(ob,m)
    return ob

def cyl(r,h,seg=12,loc=(0,0,0),axis='Z',m=None,name='cyl',r2=None):
    r2=r if r2 is None else r2
    bm=bmesh.new(); bmesh.ops.create_cone(bm,cap_ends=True,segments=seg,radius1=r,radius2=r2,depth=h)
    me=bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob=_link(me,name)
    if axis=='X': ob.rotation_euler=(0,math.radians(90),0)
    elif axis=='Y': ob.rotation_euler=(math.radians(90),0,0)
    ob.location=loc
    if m: setmat(ob,m)
    return ob

def cone(r,h,seg=8,loc=(0,0,0),m=None,name='cone'):
    return cyl(r,h,seg,loc,'Z',m,name,r2=0.0)

def wedge(sx,sy,sz,loc=(0,0,0),m=None,name='wedge'):
    # ridge runs along Y; triangular section in X-Z; sits on z=0..sz
    x=sx/2.0; y=sy/2.0
    bm=bmesh.new()
    co=[(-x,-y,0),(x,-y,0),(x,y,0),(-x,y,0),(0,-y,sz),(0,y,sz)]
    vs=[bm.verts.new(c) for c in co]
    for f in [(0,1,2,3),(0,4,5,3),(1,2,5,4),(0,1,4),(3,5,2)]:
        bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    me=bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob=_link(me,name); ob.location=loc
    if m: setmat(ob,m)
    return ob

def jitter(ob, amt, seed=1, zmin=None):
    random.seed(seed)
    for v in ob.data.vertices:
        if zmin is not None and v.co.z<=zmin: continue
        v.co.x+=random.uniform(-amt,amt); v.co.y+=random.uniform(-amt,amt); v.co.z+=random.uniform(-amt,amt)

# ---------------- scene / finalize ----------------
def new_scene():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)

def _deselect():
    for o in bpy.data.objects: o.select_set(False)

def join(objs,name):
    _deselect()
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.object.join()
    ob=bpy.context.active_object; ob.name=name
    return ob

def _tri_flat(ob):
    me=ob.data
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm,faces=bm.faces); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth=False

def _bbox(objs):
    co=[ob.matrix_world @ v.co for ob in objs for v in ob.data.vertices]
    xs=[c.x for c in co]; ys=[c.y for c in co]; zs=[c.z for c in co]
    return min(xs),max(xs),min(ys),max(ys),min(zs),max(zs)

def _apply(objs):
    _deselect()
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)

def finalize(ob, sm, origin='base'):
    _apply([ob]); bpy.context.view_layer.update()
    x0,x1,y0,y1,z0,z1=_bbox([ob])
    cx=(x0+x1)/2; cy=(y0+y1)/2; pz=z0 if origin=='base' else (z0+z1)/2
    cur=bpy.context.scene.cursor.location.copy()
    bpy.context.scene.cursor.location=(cx,cy,pz)
    _deselect(); ob.select_set(True); bpy.context.view_layer.objects.active=ob
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.context.scene.cursor.location=cur
    ob.location=(0,0,0)
    _tri_flat(ob); ob.name=sm
    return export([ob],sm), len(ob.data.polygons)

def finalize_set(objs, sm, origin='base'):
    _apply(objs); bpy.context.view_layer.update()
    x0,x1,y0,y1,z0,z1=_bbox(objs)
    cx=(x0+x1)/2; cy=(y0+y1)/2; pz=z0 if origin=='base' else (z0+z1)/2
    sh=Vector((-cx,-cy,-pz))
    for ob in objs: ob.location+=sh
    for ob in objs: _tri_flat(ob)
    tris=sum(len(o.data.polygons) for o in objs)
    return export(objs,sm), tris

def export(objs, sm):
    _deselect()
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    path=os.path.join(EXPORT_DIR, sm+'.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True, object_types={'MESH'},
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        add_leaf_bones=False, path_mode='COPY', embed_textures=False,
        axis_forward='-Z', axis_up='Y')
    return path

# ---------------- registry ----------------
REG={}  # "cat|name" -> (cat, name, fn, origin, is_set)
def asset(cat,name,origin='base',is_set=False):
    def deco(fn): REG[cat+'|'+name]=(cat,name,fn,origin,is_set); return fn
    return deco

# =========================================================
#  ASSET BUILDERS
# =========================================================

# ---- A. Terrain ----
@asset('Tile','GroundTile')
def a1():
    top=box(2,2,0.18,(0,0,0.09),mat('grass'),'top')
    soil=box(2.0,2.0,0.14,(0,0,-0.02),mat('dirt'),'soil')
    return join([top,soil],'x')

@asset('Tile','IslandEdge')
def a2():
    top=box(2,2,0.18,(0,0,0.09),mat('grass'),'top')
    # tapered soil underside
    b=box(2.0,2.0,1.5,(0,0,-0.75),mat('dirt'),'soil')
    for v in b.data.vertices:
        if v.co.z< -0.4:  # taper bottom inward
            v.co.x*=0.6; v.co.y*=0.6
    jitter(b,0.06,seed=3,zmin=-0.01)
    return join([top,b],'x')

@asset('Tile','WaterTile')
def a3():
    w=box(2,2,0.08,(0,0,0.04),mat('cyan_water'),'w')
    for v in w.data.vertices:
        if v.co.z>0: v.co.z+=random.Random(int(v.co.x*7+v.co.y*3)).uniform(-0.02,0.03)
    return w

# ---- B. Mountains ----
def _mountain_body(r,h,seg,seed,colkey,jit):
    m=cone(r,h,seg,(0,0,h/2),mat(colkey),'peak')
    # add mid ledge ring by inset? keep simple: jitter upper verts for facets
    jitter(m,jit,seed=seed,zmin=0.05)
    return m

@asset('Mountain','Rocky')
def b1():
    body=_mountain_body(5,8,8,11,'rock',0.5)
    base=cyl(5.2,1.4,8,(0,0,0.7),m=mat('rock_dk'),name='base')
    jitter(base,0.35,seed=5,zmin=0.05)
    ledge=box(3.2,1.6,0.6,(0,-3.2,1.2),mat('rock_dk'),'ledge')  # flat front area
    return join([body,base,ledge],'x')

@asset('Mountain','Snowy')
def b2():
    body=cone(5,9,8,(0,0,4.5),mat('rock'),'peak'); jitter(body,0.5,seed=7,zmin=0.05)
    base=cyl(5.2,1.4,8,(0,0,0.7),m=mat('rock_dk'),name='base'); jitter(base,0.3,seed=8,zmin=0.05)
    cap=cone(2.6,3.2,8,(0,0,7.0),mat('snow'),'cap'); jitter(cap,0.25,seed=9,zmin=6.2)
    ledge=box(3.0,1.5,0.6,(0,-3.1,1.2),mat('rock_dk'),'ledge')
    return join([body,base,cap,ledge],'x')

@asset('Mountain','Volcanic')
def b3():
    body=cyl(5,8,8,(0,0,4),m=mat('coal'),name='peak',r2=1.6); jitter(body,0.5,seed=13,zmin=0.05)
    base=cyl(5.2,1.3,8,(0,0,0.65),m=mat('dark'),name='base'); jitter(base,0.3,seed=14,zmin=0.05)
    rim=cyl(1.9,0.7,8,(0,0,8.1),m=mat('rock_dk'),name='rim')
    lava=cyl(1.5,0.3,8,(0,0,8.0),m=mat('orange'),name='lava')
    v1=box(0.35,0.35,4.5,(1.6,-1.2,4.0),mat('orange'),'vein'); v1.rotation_euler=(0.1,0.25,0)
    v2=box(0.3,0.3,4.0,(-1.8,-0.6,3.6),mat('orange'),'vein2'); v2.rotation_euler=(-0.1,-0.2,0)
    return join([body,base,rim,lava,v1,v2],'x')

@asset('Mountain','Crystal')
def b4():
    body=_mountain_body(5,8,8,17,'rock',0.5)
    base=cyl(5.2,1.4,8,(0,0,0.7),m=mat('rock_dk'),name='base'); jitter(base,0.3,seed=18,zmin=0.05)
    cm=mat('emerald')
    shards=[]
    random.seed(21)
    for i in range(5):
        a=i*1.25; rr=2.6
        s=cone(0.5,random.uniform(2.0,3.5),5,(math.cos(a)*rr,math.sin(a)*rr,2.2),cm,'shard%d'%i)
        s.rotation_euler=(random.uniform(-0.3,0.3),random.uniform(-0.3,0.3),0)
        shards.append(s)
    return join([body,base]+shards,'x')

@asset('Mountain','MineEntrance')
def b5():
    dark=mat('dark'); wd=mat('wood')
    opening=box(1.6,0.5,2.0,(0,0.2,1.0),dark,'hole')
    lpost=box(0.3,0.3,2.2,(-1.0,-0.2,1.1),wd,'lp')
    rpost=box(0.3,0.3,2.2,(1.0,-0.2,1.1),wd,'rp')
    lintel=box(2.6,0.35,0.35,(0,-0.2,2.25),wd,'lintel')
    aframeL=box(0.25,0.25,2.6,(-1.1,-0.2,1.3),wd,'af1'); aframeL.rotation_euler=(0,math.radians(20),0)
    aframeR=box(0.25,0.25,2.6,(1.1,-0.2,1.3),wd,'af2'); aframeR.rotation_euler=(0,math.radians(-20),0)
    rail1=box(0.08,1.4,0.08,(-0.35,-1.0,0.1),mat('steel'),'r1')
    rail2=box(0.08,1.4,0.08,(0.35,-1.0,0.1),mat('steel'),'r2')
    tie=box(1.0,0.12,0.08,(0,-0.7,0.05),wd,'tie')
    return join([opening,lpost,rpost,lintel,aframeL,aframeR,rail1,rail2,tie],'x')

# ---- C. Buildings ----
@asset('Building','Storage')
def c1():
    walls=box(3.2,2.2,1.6,(0,0,0.8),mat('dirt'),'walls')
    roof=wedge(3.4,2.4,0.9,(0,0,1.6),mat('steel'),'roof')
    door=box(1.1,0.06,1.2,(0,-1.12,0.6),mat('rock_dk'),'door')
    # ore bays beside
    b1w=box(1.4,0.12,0.5,(2.4,0,0.25),mat('wood'),'bay')
    b2w=box(0.12,2.0,0.5,(1.75,0,0.25),mat('wood'),'bay2')
    b3w=box(0.12,2.0,0.5,(3.05,0,0.25),mat('wood'),'bay3')
    floor=box(1.4,2.0,0.1,(2.4,0,0.05),mat('dirt'),'bayfloor')
    return join([walls,roof,door,b1w,b2w,b3w,floor],'x')

@asset('Building','Refinery')
def c2():
    body=box(3.0,2.4,2.0,(0,0,1.0),mat('steel'),'body')
    roof=box(3.05,2.45,0.2,(0,0,2.05),mat('rock_dk'),'roof')
    chimney=cyl(0.4,2.2,10,(0.9,0.6,3.0),m=mat('steel'),name='chim')
    ctop=cyl(0.48,0.3,10,(0.9,0.6,4.1),m=mat('rock_dk'),name='ctop')
    stripe=box(3.02,2.42,0.3,(0,0,0.35),mat('orange'),'stripe')
    pipe1=cyl(0.14,2.0,8,(-1.4,-0.6,1.0),m=mat('gold_warm_dk'),name='pipe')
    pipe2=cyl(0.14,1.6,8,(-1.4,0.2,0.8),m=mat('gold_warm_dk'),name='pipe2')
    conv=box(1.0,0.6,0.2,(0,-1.5,0.5),mat('rock_dk'),'conv')
    return join([body,roof,chimney,ctop,stripe,pipe1,pipe2,conv],'x')

@asset('Building','Market')
def c3():
    walls=box(2.6,2.2,1.5,(0,0,0.75),mat('gold_warm'),'walls')
    roof=wedge(2.9,2.5,1.0,(0,0,1.5),mat('red_roof'),'roof')
    awn=box(2.4,0.8,0.08,(0,-1.35,1.25),mat('red_roof'),'awn'); awn.rotation_euler=(math.radians(18),0,0)
    counter=box(2.0,0.3,0.7,(0,-1.15,0.35),mat('wood'),'counter')
    post=box(0.12,0.12,1.6,(1.5,-1.5,0.8),mat('wood'),'post')
    sign=box(0.9,0.08,0.5,(1.5,-1.5,1.45),mat('wood'),'sign')
    crate1=box(0.4,0.4,0.4,(-1.4,-1.4,0.2),mat('wood'),'cr1')
    crate2=box(0.35,0.35,0.35,(-1.0,-1.5,0.17),mat('wood'),'cr2')
    return join([walls,roof,awn,counter,post,sign,crate1,crate2],'x')

@asset('Building','Station')
def c4():
    plat=box(4.0,1.6,0.5,(0,0,0.25),mat('concrete'),'plat')
    p1=box(0.15,0.15,1.4,(-1.4,0.4,1.2),mat('wood'),'p1')
    p2=box(0.15,0.15,1.4,(1.4,0.4,1.2),mat('wood'),'p2')
    roof=wedge(3.4,1.4,0.6,(0,0.4,1.9),mat('red_roof'),'roof')
    board=box(1.2,0.08,0.5,(0,-0.2,1.3),mat('wood'),'board')
    chute=box(0.7,0.7,0.12,(1.2,-0.6,0.9),mat('steel'),'chute'); chute.rotation_euler=(math.radians(30),0,0)
    return join([plat,p1,p2,roof,board,chute],'x')

@asset('Building','LoadingDock')
def c5():
    bay=box(2.2,2.0,0.6,(0,0,0.3),mat('concrete'),'bay')
    ramp=wedge(1.4,1.0,0.6,(0,-1.4,0),mat('concrete'),'ramp'); ramp.rotation_euler=(0,0,math.radians(90))
    boll1=cyl(0.12,0.6,8,(-0.8,-0.9,0.3),m=mat('gold_warm'),name='b1')
    boll2=cyl(0.12,0.6,8,(0.8,-0.9,0.3),m=mat('gold_warm'),name='b2')
    frame=box(0.15,0.15,1.8,(1.0,0.6,0.9),mat('steel'),'fr')
    hopper=cone(0.6,0.7,6,(0.4,0.6,1.4),mat('steel'),'hop'); hopper.rotation_euler=(math.radians(180),0,0)
    return join([bay,ramp,boll1,boll2,frame,hopper],'x')

# ---- D. Vehicles ----
def _wheel(r,loc,name):
    w=cyl(r,0.18,10,loc,'X',mat('dark'),name); return w

@asset('Vehicle','TrainEngine','base')
def d1():
    body=mat('train_green'); trim=mat('rock_dk')
    boiler=cyl(0.6,1.5,12,(0,-0.5,0.75),'Y',body,'boiler')
    cab=box(1.1,0.9,1.1,(0,0.9,0.85),body,'cab')
    cabroof=box(1.2,1.0,0.12,(0,0.9,1.45),trim,'cr')
    front=cyl(0.62,0.2,12,(0,-1.3,0.75),'Y',trim,'front')
    chim=cyl(0.16,0.5,8,(0,-1.0,1.5),m=mat('dark'),name='chim')
    dome=cyl(0.22,0.25,8,(0,-0.3,1.45),m=trim,name='dome')
    catch=wedge(1.0,0.5,0.5,(0,-1.55,0.1),trim,'catch'); catch.rotation_euler=(0,0,math.radians(90))
    base=box(1.3,2.6,0.2,(0,-0.2,0.2),trim,'base')
    ws=[_wheel(0.32,(0.62,-0.9,0.3),'w1'),_wheel(0.32,(-0.62,-0.9,0.3),'w2'),
        _wheel(0.42,(0.62,0.4,0.35),'w3'),_wheel(0.42,(-0.62,0.4,0.35),'w4')]
    return join([boiler,cab,cabroof,front,chim,dome,catch,base]+ws,'x')

@asset('Vehicle','OreWagon','base')
def d2():
    body=mat('steel'); dk=mat('rock_dk')
    floor=box(1.4,1.7,0.15,(0,0,0.45),body,'floor')
    def wall(sx,sy,loc,rot):
        w=box(sx,sy,0.7,loc,body,'wall'); w.rotation_euler=rot; return w
    wl=wall(0.12,1.7,(-0.72,0,0.75),(0,math.radians(12),0))
    wr=wall(0.12,1.7,(0.72,0,0.75),(0,math.radians(-12),0))
    wf=wall(1.5,0.12,(0,-0.85,0.75),(math.radians(-12),0,0))
    wb=wall(1.5,0.12,(0,0.85,0.75),(math.radians(12),0,0))
    cf=box(0.2,0.3,0.2,(0,-1.05,0.5),dk,'cf')
    cb=box(0.2,0.3,0.2,(0,1.05,0.5),dk,'cb')
    ws=[_wheel(0.3,(0.6,-0.6,0.28),'w1'),_wheel(0.3,(-0.6,-0.6,0.28),'w2'),
        _wheel(0.3,(0.6,0.6,0.28),'w3'),_wheel(0.3,(-0.6,0.6,0.28),'w4')]
    return join([floor,wl,wr,wf,wb,cf,cb]+ws,'x')

@asset('Vehicle','OreTruck','base')
def d3():
    cab=mat('truck_blue'); bed=mat('rock_dk')
    cabin=box(0.9,0.8,0.8,(0,-0.6,0.75),cab,'cab')
    hood=box(0.9,0.5,0.45,(0,-1.15,0.55),cab,'hood')
    bedbox=box(1.0,1.1,0.55,(0,0.55,0.7),bed,'bed')
    for v in bedbox.data.vertices:  # hollow-ish: raise inner floor look via keeping simple
        pass
    chassis=box(1.0,2.2,0.2,(0,0,0.35),mat('dark'),'ch')
    ws=[_wheel(0.28,(0.5,-0.7,0.28),'w1'),_wheel(0.28,(-0.5,-0.7,0.28),'w2'),
        _wheel(0.28,(0.5,0.6,0.28),'w3'),_wheel(0.28,(-0.5,0.6,0.28),'w4')]
    return join([cabin,hood,bedbox,chassis]+ws,'x')

@asset('Vehicle','CargoTruck','base')
def d4():
    cab=mat('orange'); wood=mat('wood')
    cabin=box(0.9,0.8,0.85,(0,-0.75,0.8),cab,'cab')
    hood=box(0.9,0.5,0.45,(0,-1.3,0.55),cab,'hood')
    flat=box(1.0,1.4,0.12,(0,0.5,0.62),wood,'flat')
    railL=box(0.08,1.4,0.25,(-0.48,0.5,0.8),wood,'rl')
    railR=box(0.08,1.4,0.25,(0.48,0.5,0.8),wood,'rr')
    railB=box(1.0,0.08,0.25,(0,1.18,0.8),wood,'rb')
    chassis=box(1.0,2.4,0.2,(0,0,0.35),mat('dark'),'ch')
    ws=[_wheel(0.28,(0.5,-0.85,0.28),'w1'),_wheel(0.28,(-0.5,-0.85,0.28),'w2'),
        _wheel(0.28,(0.5,0.7,0.28),'w3'),_wheel(0.28,(-0.5,0.7,0.28),'w4')]
    return join([cabin,hood,flat,railL,railR,railB,chassis]+ws,'x')

# ---- E. Rail & Road ----
def _rail_straight(off=(0,0)):
    ox,oy=off; parts=[]
    parts.append(box(1.9,0.5,0.12,(ox,oy,0.06),mat('gravel'),'bed'))
    for tx in [-0.6,-0.2,0.2,0.6]:
        parts.append(box(0.5,0.14,0.08,(ox,oy+tx,0.14),mat('wood'),'tie'))
    parts.append(box(0.07,1.9,0.09,(ox-0.28,oy,0.2),mat('steel'),'rail'))
    parts.append(box(0.07,1.9,0.09,(ox+0.28,oy,0.2),mat('steel'),'rail'))
    return join(parts,'RailStraight')

# ---- F. Ore ----
@asset('Ore','Chunk','center')
def f1():
    rock=box(0.4,0.4,0.32,(0,0,0.16),mat('iron'),'rock')
    jitter(rock,0.06,seed=31)
    fac=mat('copper')
    facs=[]
    random.seed(33)
    for i in range(3):
        f=box(0.12,0.12,0.06,(random.uniform(-0.1,0.1),random.uniform(-0.1,0.1),0.30),fac,'f%d'%i)
        f.rotation_euler=(random.uniform(0,0.4),random.uniform(0,0.4),random.uniform(0,1))
        facs.append(f)
    return join([rock]+facs,'x')

# ---- C6 Building add-on kit (set) ----
@asset('Building','TierKit','base',is_set=True)
def c6():
    st=mat('steel'); dk=mat('rock_dk'); wd=mat('wood'); go=mat('gold_warm')
    chimney=cyl(0.3,1.6,10,(-2.4,0,0.8),m=st,name='SM_addon_chimney')
    setmat(cyl(0.36,0.2,10,(-2.4,0,1.5),m=dk,name='ct'),dk)
    wing=box(1.4,1.2,1.0,(-0.6,0,0.5),mat('dirt'),'SM_addon_wing')
    sign=box(1.4,0.1,0.7,(1.0,0,1.1),go,'SM_addon_sign')
    setmat(box(0.1,0.1,1.1,(0.5,0,0.55),wd,'sp1'),wd)
    setmat(box(0.1,0.1,1.1,(1.5,0,0.55),wd,'sp2'),wd)
    tank=cyl(0.5,0.8,12,(2.6,0,0.9),'Y',st,'SM_addon_tank')
    lights=box(1.2,0.05,0.05,(3.8,0,0.9),go,'SM_addon_lights')
    return [chimney,wing,sign,tank,lights]

# ---- E. Rail & Road (each piece its own asset) ----
def _rail_pieces(along='Y'):
    parts=[]
    parts.append(box(0.6,1.9,0.12,(0,0,0.06),mat('gravel'),'bed'))
    for t in [-0.7,-0.35,0,0.35,0.7]:
        parts.append(box(0.56,0.14,0.08,(0,t,0.15),mat('wood'),'tie'))
    parts.append(box(0.08,1.9,0.09,(-0.22,0,0.21),mat('steel'),'rail'))
    parts.append(box(0.08,1.9,0.09,(0.22,0,0.21),mat('steel'),'rail'))
    return parts

@asset('Rail','Straight','center')
def e1():
    return join(_rail_pieces(),'x')

@asset('Rail','Curve','center')
def e2():
    # quarter arc radius R connecting -Y side to +X side, centre of arc at (R,-R)
    R=1.0; cxp,cyp=R,-R; parts=[]
    n=6
    for i in range(n+1):
        a=math.radians(90)*(i/n)+math.radians(180)  # from 180deg(-X rel) sweep
        # tie positions along arc
        ang=math.radians(90)*(i/n)
        px=cxp - R*math.cos(ang); py=cyp + R*math.sin(ang)
        t=box(0.56,0.14,0.08,(px,py,0.15),mat('wood'),'tie'); t.rotation_euler=(0,0,ang)
        parts.append(t)
    # gravel bed as thick low arc approximation (few boxes)
    for i in range(n):
        ang=math.radians(90)*((i+0.5)/n)
        px=cxp - R*math.cos(ang); py=cyp + R*math.sin(ang)
        b=box(0.6,0.4,0.12,(px,py,0.06),mat('gravel'),'bed'); b.rotation_euler=(0,0,ang); parts.append(b)
    # rails inner/outer
    for rr,nm in [(R-0.22,'ri'),(R+0.22,'ro')]:
        for i in range(n):
            ang=math.radians(90)*((i+0.5)/n)
            px=cxp - rr*math.cos(ang); py=cyp + rr*math.sin(ang)
            seg=box(0.08,(math.pi/2*rr)/n+0.04,0.09,(px,py,0.21),mat('steel'),nm); seg.rotation_euler=(0,0,ang); parts.append(seg)
    return join(parts,'x')

@asset('Rail','Junction','center')
def e3():
    parts=_rail_pieces()
    # branch stub to +X
    parts.append(box(0.9,0.6,0.12,(0.75,0,0.06),mat('gravel'),'bbed'))
    parts.append(box(0.9,0.08,0.09,(0.75,-0.22,0.21),mat('steel'),'br'))
    parts.append(box(0.9,0.08,0.09,(0.75,0.22,0.21),mat('steel'),'br2'))
    return join(parts,'x')

@asset('Rail','Buffer','center')
def e4():
    parts=_rail_pieces()
    parts.append(box(0.7,0.3,0.5,(0,0.9,0.3),mat('rock_dk'),'stop'))
    parts.append(box(0.7,0.15,0.15,(0,0.75,0.5),mat('red_roof'),'pad'))
    return join(parts,'x')

@asset('Road','Straight','center')
def e5():
    surf=box(2.0,2.0,0.08,(0,0,0.04),mat('dirt'),'surf')
    r1=box(0.18,2.0,0.02,(-0.45,0,0.09),mat('wood'),'rut'); r2=box(0.18,2.0,0.02,(0.45,0,0.09),mat('wood'),'rut2')
    return join([surf,r1,r2],'x')

@asset('Road','Curve','center')
def e6():
    R=1.0; cxp,cyp=R,-R; parts=[]; n=6
    for i in range(n):
        ang=math.radians(90)*((i+0.5)/n)
        px=cxp - R*math.cos(ang); py=cyp + R*math.sin(ang)
        b=box(0.9,0.42,0.08,(px,py,0.04),mat('dirt'),'seg'); b.rotation_euler=(0,0,ang); parts.append(b)
    return join(parts,'x')

# ---- F2 / F3 ----
@asset('Ore','GemCluster','center')
def f2():
    base=box(0.4,0.4,0.14,(0,0,0.07),mat('rock'),'base'); jitter(base,0.04,seed=41)
    cm=mat('ruby'); shards=[]; random.seed(43)
    for i in range(4):
        h=random.uniform(0.3,0.6)
        s=cone(0.09,h,5,(random.uniform(-0.12,0.12),random.uniform(-0.12,0.12),0.14+h/2),cm,'s%d'%i)
        s.rotation_euler=(random.uniform(-0.2,0.2),random.uniform(-0.2,0.2),random.uniform(0,1))
        shards.append(s)
    return join([base]+shards,'x')

def _pile(scale,seed):
    heap=cyl(0.5*scale,0.35*scale,7,(0,0,0.17*scale),m=mat('iron'),name='heap',r2=0.1*scale)
    jitter(heap,0.06*scale,seed=seed,zmin=0.02)
    chunks=[]; random.seed(seed)
    for i in range(4):
        c=box(0.16*scale,0.16*scale,0.14*scale,(random.uniform(-0.25,0.25)*scale,random.uniform(-0.25,0.25)*scale,0.3*scale),mat('iron'),'c%d'%i)
        c.rotation_euler=(random.uniform(0,1),random.uniform(0,1),random.uniform(0,1)); chunks.append(c)
    return join([heap]+chunks,'x')

@asset('Ore','PileSmall')
def f3s(): return _pile(0.7,51)
@asset('Ore','PileMed')
def f3m(): return _pile(1.0,52)
@asset('Ore','PileLarge')
def f3l(): return _pile(1.4,53)

# ---- G. Refined products ----
@asset('Product','Coke')
def g1():
    dk=mat('coal'); parts=[];
    layout=[(-0.2,-0.2),(0.0,-0.2),(0.2,-0.2),(-0.1,0.0),(0.1,0.0),(0,0.18)]
    zs=[0.1,0.1,0.1,0.28,0.28,0.46]
    for (x,y),z in zip(layout,zs):
        parts.append(box(0.18,0.18,0.18,(x,y,z),dk,'b'))
    return join(parts,'x')

@asset('Product','MetalBar')
def g2():
    bar=box(0.5,0.24,0.16,(0,0,0.08),mat('gold',metallic=0.4,rough=0.35),'bar')
    for v in bar.data.vertices:
        if v.co.z>0: v.co.x*=0.82; v.co.y*=0.82  # trapezoidal
    return bar

@asset('Product','SteelBeam','center')
def g3():
    st=mat('steel',metallic=0.3,rough=0.4)
    top=box(0.4,1.6,0.08,(0,0,0.36),st,'t'); bot=box(0.4,1.6,0.08,(0,0,-0.36),st,'b'); web=box(0.1,1.6,0.72,(0,0,0),st,'w')
    return join([top,bot,web],'x')

def _octa(r,ht,hb,seg,m,name):
    up=cone(r,ht,seg,(0,0,hb+ht/2),m,name+'u')
    dn=cone(r,hb,seg,(0,0,hb/2),m,name+'d'); dn.rotation_euler=(math.radians(180),0,0)
    return join([up,dn],name)

@asset('Product','CutGem')
def g4():
    gm=mat('emerald',rough=0.25)
    top=cyl(0.22,0.12,8,(0,0,0.28),m=gm,name='tbl',r2=0.28)  # crown
    pav=cone(0.28,0.32,8,(0,0,0.16),gm,'pav'); pav.rotation_euler=(math.radians(180),0,0)
    return join([top,pav],'x')

@asset('Product','Diamond')
def g5():
    gm=mat('diamond',rough=0.15)
    table=cyl(0.18,0.06,8,(0,0,0.30),m=gm,name='tbl',r2=0.22)
    crown=cyl(0.22,0.1,8,(0,0,0.23),m=gm,name='cr',r2=0.26)
    pav=cone(0.26,0.28,8,(0,0,0.14),gm,'pav'); pav.rotation_euler=(math.radians(180),0,0)
    return join([table,crown,pav],'x')

@asset('Product','RubyRing')
def g6():
    band=cyl(0.22,0.08,16,(0,0,0.06),m=mat('gold',metallic=0.5,rough=0.3),name='band',r2=0.22)
    # make torus-ish by boolean? keep as disc ring: inner hole via scaling not trivial; use thin ring of boxes
    band2=cyl(0.14,0.1,16,(0,0,0.06),m=mat('dark'),name='hole')  # inner dark to fake hole
    gem=_octa(0.12,0.14,0.08,6,mat('ruby',rough=0.2),'gem'); gem.location=(0,0,0.28)
    prong=cyl(0.13,0.12,8,(0,0,0.18),m=mat('gold',metallic=0.5,rough=0.3),name='setting')
    return join([band,band2,prong,gem],'x')

@asset('Product','Crown')
def g7():
    go=mat('gold',metallic=0.5,rough=0.3)
    bandv=[]
    ring=cyl(0.45,0.3,12,(0,0,0.15),m=go,name='ring',r2=0.45)
    peaks=[]
    for i in range(6):
        a=i*math.radians(60)
        p=cone(0.1,0.35,4,(math.cos(a)*0.4,math.sin(a)*0.4,0.45),go,'pk%d'%i); peaks.append(p)
    gemc=_octa(0.1,0.12,0.08,6,mat('diamond',rough=0.15),'dia'); gemc.location=(0,-0.45,0.28)
    return join([ring]+peaks+[gemc],'x')

@asset('Product','Crate')
def g8():
    wd=mat('wood'); body=box(0.6,0.6,0.6,(0,0,0.3),wd,'body')
    band=box(0.62,0.62,0.16,(0,0,0.35),mat('orange'),'band')
    corners=[]
    for sx in (-1,1):
        for sy in (-1,1):
            corners.append(box(0.08,0.08,0.62,(sx*0.28,sy*0.28,0.3),mat('rock_dk'),'cn'))
    return join([body,band]+corners,'x')

# ---- H. Currency ----
@asset('Currency','Coin','center')
def h1():
    coin=cyl(0.3,0.09,16,(0,0,0),m=mat('coin',metallic=0.4,rough=0.35),name='coin')
    star=cone(0.12,0.04,5,(0,0,0.06),mat('gold_warm_dk'),'star')
    return join([coin,star],'x')

@asset('Currency','PremiumGem','center')
def h2():
    gm=mat('gem',rough=0.2)
    top=cyl(0.16,0.1,6,(0,0,0.16),m=gm,name='t',r2=0.22)
    pav=cone(0.24,0.3,6,(0,0,0.02),gm,'p'); pav.rotation_euler=(math.radians(180),0,0)
    return join([top,pav],'x')

@asset('Currency','MoneyBag')
def h3():
    bag=cyl(0.4,0.7,10,(0,0,0.35),m=mat('dirt'),name='bag',r2=0.28)
    for v in bag.data.vertices:
        if v.co.z>0.25: v.co.x*=0.5; v.co.y*=0.5
    neck=cyl(0.16,0.16,8,(0,0,0.72),m=mat('wood'),name='neck')
    sym=cone(0.12,0.04,5,(0,-0.28,0.4),mat('coin',metallic=0.4),'sym'); sym.rotation_euler=(math.radians(90),0,0)
    coins=[cyl(0.12,0.05,12,(0.35,0.1,0.05),m=mat('coin',metallic=0.4),name='c1'),
           cyl(0.12,0.05,12,(-0.3,-0.2,0.05),m=mat('coin',metallic=0.4),name='c2')]
    return join([bag,neck,sym]+coins,'x')

# ---- I. Set dressing ----
@asset('Dressing','PineTree')
def i1():
    trunk=cyl(0.12,0.5,8,(0,0,0.25),m=mat('wood'),name='trunk')
    c1=cone(0.7,0.8,8,(0,0,0.7),mat('grass_dk'),'c1')
    c2=cone(0.55,0.7,8,(0,0,1.2),mat('grass_dk'),'c2')
    c3=cone(0.38,0.6,8,(0,0,1.7),mat('grass_dk'),'c3')
    return join([trunk,c1,c2,c3],'x')

@asset('Dressing','Boulders','base',is_set=True)
def i2():
    out=[]
    for i,(sc,x) in enumerate([(0.35,-0.9),(0.55,0),(0.8,1.2)]):
        b=box(sc,sc,sc*0.8,(x,0,sc*0.4),mat('rock'),'SM_boulder_%d'%i); jitter(b,sc*0.12,seed=60+i,zmin=0.02); out.append(b)
    return out

@asset('Dressing','Bush')
def i3():
    parts=[]; random.seed(70)
    for i in range(4):
        r=random.uniform(0.2,0.32)
        s=box(r,r,r,(random.uniform(-0.2,0.2),random.uniform(-0.2,0.2),random.uniform(0.15,0.3)),mat('grass_dk'),'b%d'%i)
        jitter(s,r*0.2,seed=71+i); parts.append(s)
    return join(parts,'x')

@asset('Dressing','Cloud','center')
def i4():
    parts=[]; random.seed(80)
    for x,y,r in [(-0.6,0,0.4),(0,0.1,0.55),(0.6,0,0.42),(0.1,-0.2,0.38)]:
        s=box(r,r,r*0.8,(x,y,0),mat('snow'),'p'); jitter(s,r*0.12,seed=81); parts.append(s)
    return join(parts,'x')

@asset('Dressing','PropsKit','base',is_set=True)
def i5():
    wd=mat('wood'); st=mat('steel'); go=mat('gold_warm')
    fence=join([box(0.1,1.2,0.5,(0,0,0.4),wd,'fp'),box(0.08,0.08,0.6,(0,-0.5,0.3),wd,'fa'),box(0.08,0.08,0.6,(0,0.5,0.3),wd,'fb')],'SM_prop_fence'); fence.location=(-1.6,0,0)
    signpost=join([box(0.1,0.1,1.0,(0,0,0.5),wd,'pole'),box(0.5,0.08,0.25,(0.25,0,0.85),go,'arrow')],'SM_prop_signpost'); signpost.location=(-0.5,0,0)
    lamp=join([cyl(0.08,1.1,8,(0,0,0.55),m=st,name='pole'),box(0.2,0.2,0.2,(0,0,1.15),mat('gold'),'head')],'SM_prop_lamp'); lamp.location=(0.6,0,0)
    barrel=cyl(0.28,0.6,10,(0,0,0.3),m=wd,name='SM_prop_barrel'); barrel.location=(1.6,0,0)
    return [fence,signpost,lamp,barrel]

# ---- J. Characters ----
@asset('Character','Miner','base')
def j1():
    skin=mat('gold_warm'); cloth=mat('truck_blue'); hat=mat('gold')
    body=box(0.5,0.32,0.7,(0,0,0.85),cloth,'body')
    head=box(0.34,0.32,0.34,(0,0,1.4),skin,'head')
    hatb=cyl(0.22,0.14,10,(0,0,1.62),m=hat,name='hat')
    brim=cyl(0.3,0.04,10,(0,-0.05,1.56),m=hat,name='brim')
    la=box(0.14,0.14,0.6,(-0.32,0,0.9),cloth,'la'); ra=box(0.14,0.14,0.6,(0.32,0,0.9),cloth,'ra')
    ll=box(0.18,0.18,0.55,(-0.14,0,0.27),mat('rock_dk'),'ll'); rl=box(0.18,0.18,0.55,(0.14,0,0.27),mat('rock_dk'),'rl')
    return join([body,head,hatb,brim,la,ra,ll,rl],'x')

# =========================================================
def purge():
    _mats.clear()
    for _ in range(4):
        try: bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
        except Exception: break

def _label(txt, loc, m):
    cu=bpy.data.curves.new(txt,'FONT'); cu.body=txt; cu.size=1.1; cu.extrude=0.03
    ob=bpy.data.objects.new('L_'+txt,cu); bpy.context.collection.objects.link(ob)
    ob.location=loc; setmat(ob,m)
    return ob

def showcase():
    new_scene()
    order=['Tile','Mountain','Building','Vehicle','Rail','Road','Ore','Product','Currency','Dressing','Character']
    bycat={}
    for key,(cat,name,fn,origin,is_set) in REG.items():
        bycat.setdefault(cat,[]).append((name,fn,is_set))
    lab=mat('snow'); allobjs=[]
    y=0.0
    for cat in order:
        items=bycat.get(cat,[])
        if not items: continue
        _label(cat.upper(), (-4.5, y, 0.02), lab)
        x=0.0; rowdepth=0.0
        for name,fn,is_set in items:
            res=fn(); objs=res if isinstance(res,list) else [res]
            ob=objs[0] if len(objs)==1 else join(objs,'S_'+name)
            for poly in ob.data.polygons: poly.use_smooth=False
            bpy.context.view_layer.update()
            co=[ob.matrix_world @ v.co for v in ob.data.vertices]
            xs=[c.x for c in co]; ys=[c.y for c in co]; zs=[c.z for c in co]
            w=max(xs)-min(xs); d=max(ys)-min(ys)
            cx=(min(xs)+max(xs))/2; cy=(min(ys)+max(ys))/2
            ob.location.x += x + w/2 - cx
            ob.location.y += y + d/2 - cy
            ob.location.z += -min(zs)
            x += w + 1.3; rowdepth=max(rowdepth,d); allobjs.append(ob)
        y += rowdepth + 3.2
    # ground
    gx0=-6;
    ground=box(60,y+6,0.1,(18,y/2-2,-0.06),mat('grass_dk'),'Ground')
    # light + world
    light=bpy.data.lights.new('sun','SUN'); light.energy=3.2
    lo=bpy.data.objects.new('Sun',light); bpy.context.collection.objects.link(lo)
    lo.rotation_euler=(math.radians(55),math.radians(12),math.radians(40))
    try: bpy.context.scene.world.node_tree.nodes['Background'].inputs[0].default_value=(0.10,0.13,0.17,1)
    except: pass
    # view: material preview, angled, frame all
    for o in bpy.data.objects: o.select_set(o.type in ('MESH','FONT'))
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            for s2 in area.spaces:
                if s2.type=='VIEW_3D': s2.shading.type='MATERIAL'
            for region in area.regions:
                if region.type=='WINDOW':
                    with bpy.context.temp_override(area=area, region=region):
                        bpy.ops.view3d.view_axis(type='TOP')
                        bpy.ops.view3d.view_orbit(angle=0.75, type='ORBITDOWN')
                        bpy.ops.view3d.view_selected()
    for o in bpy.data.objects: o.select_set(False)
    return len(allobjs)

def run(names):
    lines=[]
    for nm in names:
        if nm not in REG: lines.append('%s: NOT REGISTERED'%nm); continue
        cat,name,fn,origin,is_set=REG[nm]
        try:
            new_scene()
            res=fn()
            sm='SM_%s_%s'%(cat,name)
            if is_set: path,tris=finalize_set(res,sm,origin)
            else: path,tris=finalize(res,sm,origin)
            lines.append('OK  %-28s tris=%-5d'%(sm,tris))
        except Exception:
            lines.append('ERR %s: %s'%(nm, traceback.format_exc().splitlines()[-1]))
    rep='\n'.join(lines)
    with open(REPORT,'w') as f: f.write(rep)
    print(rep)
    return rep
