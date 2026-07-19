# Ore Empire - v2 "realistic low-poly" asset builder (Blender 5.2)
# Beveled/chamfered edges, noise-displaced faceted rock, real detail. Flat-shaded, game-ready.
# Exports FBX to Assets/Art/Models_v2 (BESIDE the originals; originals untouched).
import bpy, bmesh, math, os, random, traceback
from mathutils import Vector

EXPORT_DIR = r"C:\Users\oquzt\Documents\GitHub\project_kayseri\Assets\Art\Models"
REPORT = r"C:\Users\oquzt\AppData\Local\Temp\claude\C--Users-oquzt-Desktop-BLENDER\09d1028d-9ee8-4f07-9c8b-7897aecb4f6f\scratchpad\report_v2.txt"
os.makedirs(EXPORT_DIR, exist_ok=True)

PAL = {
 'grass':'#86D06A','grass_dk':'#4E9440','dirt':'#D8B888','dirt_dk':'#B8945F','wood':'#8A5A3C','wood_dk':'#6E4429',
 'snow':'#F2F7FF','rock':'#8B8FA3','rock_dk':'#565E6B','rock_lt':'#A6ABBC','steel':'#7A879F','steel_dk':'#5A6478',
 'gold_warm':'#F2C14E','gold_warm_dk':'#C6922E','orange':'#F5923E','red_roof':'#E8663B','red_dk':'#C24A28',
 'train_green':'#2FA96B','train_green_dk':'#1F7D4E','truck_blue':'#3E7CC4','blue_dk':'#2C5C97',
 'coal':'#2B2F36','copper':'#E08A4C','iron':'#9AA0AA','silver':'#D7DCE5','gold':'#FFCF4D',
 'ruby':'#E5484D','emerald':'#2FBF71','sapphire':'#3B82F6','diamond':'#7FE3F0',
 'coin':'#FFD34E','gem':'#A855F7','dark':'#20242B','concrete':'#B9BEC7','concrete_dk':'#8F949E',
 'gravel':'#9AA0AA','cyan_water':'#7FD4E8','glass':'#3C5A73','lava':'#FF7A2F','skin':'#E8B08A',
}
def _s2l(c):
    c=c/255.0
    return c/12.92 if c<=0.04045 else ((c+0.055)/1.055)**2.4
def hexcol(h):
    h=h.lstrip('#'); r,g,b=(int(h[i:i+2],16) for i in (0,2,4))
    return (_s2l(r),_s2l(g),_s2l(b),1.0)
_mats={}
def mat(key, metallic=0.0, rough=0.6, hexoverride=None):
    name='M_'+key
    if name in _mats: return _mats[name]
    m=bpy.data.materials.new(name); m.use_nodes=True
    b=m.node_tree.nodes.get('Principled BSDF'); col=hexcol(hexoverride or PAL[key])
    b.inputs['Base Color'].default_value=col
    b.inputs['Roughness'].default_value=rough
    b.inputs['Metallic'].default_value=metallic
    try: b.inputs['Specular IOR Level'].default_value=0.25
    except: pass
    m.diffuse_color=col; _mats[name]=m; return m
def emat(key, strength=2.0, hexoverride=None):
    name='E_'+key
    if name in _mats: return _mats[name]
    m=bpy.data.materials.new(name); m.use_nodes=True
    b=m.node_tree.nodes.get('Principled BSDF'); col=hexcol(hexoverride or PAL[key])
    b.inputs['Base Color'].default_value=col
    try:
        b.inputs['Emission Color'].default_value=col
        b.inputs['Emission Strength'].default_value=strength
    except: pass
    m.diffuse_color=col; _mats[name]=m; return m

# ---------- primitives (beveled) ----------
def _link(me,name):
    ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob); return ob
def setmat(ob,m):
    ob.data.materials.clear(); ob.data.materials.append(m)

def rbox(sx,sy,sz,loc=(0,0,0),m=None,name='box',bev=0.035,seg=1,only=None):
    """Beveled box. only: iterable of edge-key filters not used; bev applied to all edges."""
    bm=bmesh.new(); bmesh.ops.create_cube(bm,size=1.0)
    for v in bm.verts: v.co.x*=sx; v.co.y*=sy; v.co.z*=sz
    w=min(bev, min(sx,sy,sz)*0.45)
    if w>0:
        bmesh.ops.bevel(bm, geom=list(bm.edges)+list(bm.verts), offset=w, segments=seg,
                        affect='EDGES', clamp_overlap=True, profile=0.72)
    me=bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob=_link(me,name); ob.location=loc
    if m: setmat(ob,m)
    return ob

def box(sx,sy,sz,loc=(0,0,0),m=None,name='box'):  # sharp box (tiling parts)
    return rbox(sx,sy,sz,loc,m,name,bev=0.0)

def rcyl(r,h,seg=16,loc=(0,0,0),axis='Z',m=None,name='cyl',r2=None,rim=0.02):
    r2=r if r2 is None else r2
    bm=bmesh.new(); bmesh.ops.create_cone(bm,cap_ends=True,segments=seg,radius1=r,radius2=r2,depth=h)
    if rim>0:
        # chamfer the top/bottom rim edges
        rim_edges=[e for e in bm.edges if abs(abs(e.verts[0].co.z)-h/2)<1e-4 and abs(abs(e.verts[1].co.z)-h/2)<1e-4]
        if rim_edges:
            bmesh.ops.bevel(bm, geom=rim_edges, offset=min(rim,h*0.3), segments=1, affect='EDGES', clamp_overlap=True)
    me=bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob=_link(me,name)
    if axis=='X': ob.rotation_euler=(0,math.radians(90),0)
    elif axis=='Y': ob.rotation_euler=(math.radians(90),0,0)
    ob.location=loc
    if m: setmat(ob,m)
    return ob

def cone(r,h,seg=8,loc=(0,0,0),m=None,name='cone'):
    return rcyl(r,h,seg,loc,'Z',m,name,r2=0.0,rim=0)

def wedge(sx,sy,sz,loc=(0,0,0),m=None,name='wedge',bev=0.03):
    x=sx/2.0; y=sy/2.0
    bm=bmesh.new()
    co=[(-x,-y,0),(x,-y,0),(x,y,0),(-x,y,0),(0,-y,sz),(0,y,sz)]
    vs=[bm.verts.new(c) for c in co]
    for f in [(0,1,2,3),(0,4,5,3),(1,2,5,4),(0,1,4),(3,5,2)]:
        bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    if bev>0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bev, segments=1, affect='EDGES', clamp_overlap=True)
    me=bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob=_link(me,name); ob.location=loc
    if m: setmat(ob,m)
    return ob

def ico(r,subd=2,loc=(0,0,0),m=None,name='ico'):
    bm=bmesh.new(); bmesh.ops.create_icosphere(bm,subdivisions=subd,radius=r)
    me=bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob=_link(me,name); ob.location=loc
    if m: setmat(ob,m)
    return ob

def prism(r,h,seg,loc=(0,0,0),m=None,name='prism'):
    return rcyl(r,h,seg,loc,'Z',m,name,rim=0)

def displace(ob, amt, seed=1, zmin=None, updown=True):
    random.seed(seed)
    for v in ob.data.vertices:
        if zmin is not None and v.co.z<=zmin: continue
        d=Vector((random.uniform(-1,1),random.uniform(-1,1),random.uniform(-1,1) if updown else 0))
        v.co += d*amt

def taper_top(ob, factor, zabove):
    for v in ob.data.vertices:
        if v.co.z>zabove: v.co.x*=factor; v.co.y*=factor

# ---------- scene / finalize ----------
def new_scene():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
def _deselect():
    for o in bpy.data.objects: o.select_set(False)
def join(objs,name):
    objs=[o for o in objs if o is not None]
    _deselect()
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.object.join()
    ob=bpy.context.active_object; ob.name=name
    return ob
def _tri_flat(ob, smooth=False):
    me=ob.data
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm,faces=bm.faces); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth=smooth
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
    ob.location=(0,0,0); _tri_flat(ob); ob.name=sm
    return export([ob],sm), len(ob.data.polygons)
def finalize_set(objs, sm, origin='base'):
    objs=[o for o in objs if o is not None]
    _apply(objs); bpy.context.view_layer.update()
    x0,x1,y0,y1,z0,z1=_bbox(objs)
    cx=(x0+x1)/2; cy=(y0+y1)/2; pz=z0 if origin=='base' else (z0+z1)/2
    sh=Vector((-cx,-cy,-pz))
    for ob in objs: ob.location+=sh
    for ob in objs: _tri_flat(ob)
    return export(objs,sm), sum(len(o.data.polygons) for o in objs)
def export(objs, sm):
    _deselect()
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    path=os.path.join(EXPORT_DIR, sm+'.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, object_types={'MESH'},
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        add_leaf_bones=False, path_mode='COPY', embed_textures=False, axis_forward='-Z', axis_up='Y')
    return path

REG={}
def asset(cat,name,origin='base',is_set=False):
    def deco(fn): REG[cat+'|'+name]=(cat,name,fn,origin,is_set); return fn
    return deco

# =====================================================================
#  BUILDERS  (Part A: terrain, mountains, buildings, rail/road)
# =====================================================================

# ---- A. Terrain ----
@asset('Tile','GroundTile')
def a1():
    top=box(2.0,2.0,0.16,(0,0,0.10),mat('grass'),'top')      # sharp footprint (tileable)
    # subtle surface facets
    displace(top,0.015,seed=2,zmin=0.1)
    soil=box(2.0,2.0,0.16,(0,0,0.0),mat('dirt'),'soil')
    # a couple of grass tufts + a pebble (inside footprint, won't break tiling)
    t1=cone(0.09,0.22,5,(0.55,-0.4,0.28),mat('grass_dk'),'tuft')
    t2=cone(0.07,0.18,5,(-0.45,0.5,0.26),mat('grass_dk'),'tuft2')
    peb=ico(0.11,1,(0.2,0.55,0.20),mat('rock'),'peb'); displace(peb,0.02,seed=4)
    return join([soil,top,t1,t2,peb],'x')

@asset('Tile','IslandEdge')
def a2():
    top=box(2.0,2.0,0.16,(0,0,1.42),mat('grass'),'top'); displace(top,0.015,seed=6,zmin=1.42)
    rock=prism(1.35,1.5,7,(0,0,0.6),mat('rock'),'rock')
    taper_top(rock,1.0,10); # taper bottom inward
    for v in rock.data.vertices:
        if v.co.z<0.3: v.co.x*=0.55; v.co.y*=0.55
    displace(rock,0.12,seed=7,zmin=0.05)
    soil=box(2.0,2.0,0.35,(0,0,1.25),mat('dirt'),'soil')
    return join([soil,top,rock],'x')

@asset('Tile','WaterTile')
def a3():
    w=box(2.0,2.0,0.10,(0,0,0.05),mat('cyan_water',rough=0.15),'w')
    random.seed(9)
    for v in w.data.vertices:
        if v.co.z>0.05: v.co.z+=random.uniform(-0.015,0.03)
    # low wave facets
    wv=ico(0.4,1,(0.4,0.3,0.08),mat('cyan_water',rough=0.1,hexoverride='#9BE0F0'),'wave')
    for v in wv.data.vertices: v.co.z*=0.25
    return join([w,wv],'x')

# ---- B. Mountains ----
def _rock_mass(rx,ry,h,seed,colkey,subd=2,disp=0.9,flatfront=True,peak=0.78):
    m=ico(1.0,subd,(0,0,0),mat(colkey),'rock')
    for v in m.data.vertices:
        t=(v.co.z*0.5+0.5)                 # 0 base .. 1 top
        shrink=1.0-(t**1.15)*peak          # conical: narrower toward the peak
        v.co.x*=rx*shrink; v.co.y*=ry*shrink; v.co.z=t*h
    # craggier near the top
    random.seed(seed)
    for v in m.data.vertices:
        if v.co.z>0.2:
            f=disp*(0.5+0.9*(v.co.z/h))
            v.co.x+=random.uniform(-1,1)*f; v.co.y+=random.uniform(-1,1)*f; v.co.z+=random.uniform(-1,1)*f*0.7
    for v in m.data.vertices:
        if v.co.z<0.05: v.co.z=0.0
    if flatfront:
        for v in m.data.vertices:
            if v.co.y< -ry*0.5 and v.co.z<h*0.3: v.co.y=-ry*0.5
    return m

# --- train tunnel entrance built into the mountain foot ---
def _train_portal(fyf, framekey, seed):
    dark=mat('dark'); fr=mat(framekey); st=mat('steel'); wd=mat('wood')
    parts=[]
    parts.append(box(1.9,1.9,2.0,(0,fyf+1.05,1.0),dark,'tunnel'))         # dark recess
    arch=rcyl(1.0,1.9,12,(0,fyf+1.05,2.0),'Y',dark,'arch',rim=0); arch.scale=(1,1,0.55); parts.append(arch)
    parts.append(rbox(0.45,0.6,2.5,(-1.2,fyf,1.25),fr,'pilL',bev=0.05))   # framed pillars
    parts.append(rbox(0.45,0.6,2.5,(1.2,fyf,1.25),fr,'pilR',bev=0.05))
    for i in range(5):                                                    # arch ring blocks
        ang=math.radians(180)*(i/4.0)
        x=math.cos(ang)*1.2; z=2.5+math.sin(ang)*1.05
        b=box(0.55,0.6,0.45,(x,fyf,z),fr,'ab%d'%i); b.rotation_euler=(0,-(ang-math.pi/2),0); parts.append(b)
    parts.append(box(0.5,0.6,0.5,(0,fyf,3.6),fr,'key'))                   # keystone
    for x in (-0.34,0.34):                                                # rails out of tunnel
        parts.append(box(0.09,2.6,0.12,(x,fyf-1.1,0.12),st,'rail'))
    for k in range(5):
        parts.append(box(1.05,0.16,0.10,(0,fyf+0.2-k*0.55,0.09),wd,'tie'))
    return parts

# --- embedded ore deposit (metal vein or gem cluster) ---
def _ore_deposit(orekey, style, loc, seed):
    random.seed(seed); parts=[]
    parts.append(rbox(0.6,0.5,0.5,loc,mat('rock_dk'),'mx',bev=0.03))
    if style=='gem':
        cm=mat(orekey,rough=0.15)
        for i in range(5):
            hh=random.uniform(0.35,0.65)
            s=cone(0.11,hh,5,(loc[0]+random.uniform(-0.18,0.18),loc[1]+random.uniform(-0.12,0.12),loc[2]+0.15+hh/2),cm,'g%d'%i)
            s.rotation_euler=(random.uniform(-0.35,0.35),random.uniform(-0.35,0.35),random.uniform(0,1)); parts.append(s)
    else:
        met=0.5 if orekey in ('copper','iron','silver','gold') else 0.0
        cm=mat(orekey,metallic=met,rough=0.3)
        for i in range(4):
            r=random.uniform(0.18,0.26)
            n=box(r,r,r*0.85,(loc[0]+random.uniform(-0.2,0.2),loc[1]+random.uniform(-0.12,0.12),loc[2]+random.uniform(0.0,0.24)),cm,'n%d'%i)
            n.rotation_euler=(random.uniform(0,1),random.uniform(0,1),random.uniform(0,1)); parts.append(n)
    return join(parts,'ore')

def _ore_mountain(rockkey, orekey, style, snow=False, lava=False, seed=1):
    rx=4.8; ry=4.4; h=8.4; fyf=-3.6
    body=_rock_mass(rx,ry,h,seed,rockkey,subd=2,disp=1.15,flatfront=False,peak=0.82)
    # flatten just the portal footprint so the tunnel mouth sits flush
    for v in body.data.vertices:
        if v.co.y<-3.4 and abs(v.co.x)<1.5 and v.co.z<2.9: v.co.y=-3.4
    sh=_rock_mass(2.4,2.2,4.6,seed+5,rockkey,subd=1,disp=0.6,flatfront=False,peak=0.82); sh.location=(2.6,1.5,0.0)
    skirtkey='dark' if rockkey=='coal' else 'rock_dk'
    skirt=prism(rx*1.06,1.3,9,(0,0,0.62),mat(skirtkey),'skirt'); displace(skirt,0.35,seed=seed+1,zmin=0.05)
    parts=[skirt,body,sh]
    parts+=_train_portal(fyf, skirtkey, seed)
    # ore deposits protruding from the visible surfaces + piles at the tunnel mouth
    spots=[(-2.2,-3.5,2.3),(2.2,-3.5,2.5),(3.9,0.4,2.0),(-3.9,0.6,2.2),
           (-1.6,-4.0,0.4),(1.7,-4.0,0.4)]
    for i,loc in enumerate(spots):
        parts.append(_ore_deposit(orekey,style,loc,seed+11+i))
    if snow:
        cap=ico(1.0,2,(0,0,0),mat('snow'),'cap')
        for v in cap.data.vertices:
            v.co.x*=2.5; v.co.y*=2.3; v.co.z=(v.co.z*0.5+0.5)*3.0+(h-3.0)
        displace(cap,0.26,seed=seed+2,zmin=h-2.6)
        for v in cap.data.vertices:
            if v.co.z<h-2.9: v.co.z=h-2.9
        parts.append(cap)
    if lava:
        for v in body.data.vertices:
            if v.co.z>h-1.5 and (v.co.x**2+(v.co.y-0.5)**2)<3.0: v.co.z-=1.3
        parts.append(rcyl(1.3,0.5,10,(0,0.5,h-2.4),m=emat('lava',3.0),name='lava',r2=1.0,rim=0))
        random.seed(seed+3)
        for i in range(5):
            a=-1.4+i*0.7                        # spread across the front/sides
            vn=rbox(0.22,0.22,random.uniform(3.0,4.2),(math.sin(a)*3.0,-math.cos(a)*3.0,3.2),emat('lava',2.2),'lv%d'%i,bev=0.04)
            vn.rotation_euler=(random.uniform(-0.15,0.15),random.uniform(-0.15,0.15),a); parts.append(vn)
    return join(parts,'x')

# one mountain per ore tier: name, rock, ore, style, snow, lava, seed
ORE_CFG=[
 ('Coal','coal','coal','vein',False,False,101),
 ('Copper','rock','copper','vein',False,False,102),
 ('Iron','rock','iron','vein',False,False,103),
 ('Silver','rock','silver','vein',True,False,104),
 ('Gold','rock','gold','vein',False,False,105),
 ('Ruby','coal','ruby','gem',False,True,106),
 ('Emerald','rock','emerald','gem',False,False,107),
 ('Sapphire','rock','sapphire','gem',False,False,108),
 ('Diamond','rock','diamond','gem',True,False,109),
]
def _mk(rk,ok,st,sn,lv,sd):
    return lambda: _ore_mountain(rk,ok,st,sn,lv,sd)
for _nm,_rk,_ok,_st,_sn,_lv,_sd in ORE_CFG:
    REG['Mountain|'+_nm]=('Mountain',_nm,_mk(_rk,_ok,_st,_sn,_lv,_sd),'base',False)

@asset('Mountain','MineEntrance')
def b5():
    dark=mat('dark'); wd=mat('wood'); wdk=mat('wood_dk'); st=mat('steel_dk')
    archtop=rcyl(0.9,0.55,8,(0,0.1,2.0),'Y',dark,'arch',rim=0); archtop.scale=(1,1,0.6)
    hole=box(1.6,0.55,2.0,(0,0.1,1.0),dark,'hole')
    lpost=rbox(0.32,0.34,2.5,(-1.05,-0.15,1.25),wd,'lp',bev=0.04)
    rpost=rbox(0.32,0.34,2.5,(1.05,-0.15,1.25),wd,'rp',bev=0.04)
    lintel=rbox(2.7,0.4,0.4,(0,-0.15,2.55),wd,'lintel',bev=0.05)
    braceL=box(0.2,0.2,1.3,(-1.0,-0.15,2.2),wdk,'brc'); braceL.rotation_euler=(0,math.radians(35),0)
    braceR=box(0.2,0.2,1.3,(1.0,-0.15,2.2),wdk,'brc2'); braceR.rotation_euler=(0,math.radians(-35),0)
    rails=[box(0.07,1.6,0.10,(x,-1.0,0.14),st,'rl') for x in (-0.32,0.32)]
    ties=[box(0.9,0.14,0.09,(0,y,0.09),wdk,'tie') for y in (-0.5,-1.1)]
    return join([hole,archtop,lpost,rpost,lintel,braceL,braceR]+rails+ties,'x')

# ---- C. Buildings ----
def _window(w,h,loc,rot=(0,0,0),frame=None,glass=None):
    frame=frame or mat('wood_dk'); glass=glass or mat('glass')
    fr=rbox(w,0.08,h,loc,frame,'winf',bev=0.02)
    gl=rbox(w*0.7,0.06,h*0.7,(loc[0],loc[1]-0.03,loc[2]),glass,'wing',bev=0.01)
    o=join([fr,gl],'win'); o.rotation_euler=rot; return o

@asset('Building','Storage')
def c1():
    wall=mat('dirt'); trim=mat('wood_dk'); roofm=mat('steel')
    base=rbox(3.4,2.4,0.25,(0,0,0.12),mat('concrete_dk'),'found',bev=0.03)
    walls=rbox(3.2,2.2,1.7,(0,0,1.1),wall,'walls',bev=0.05)
    roof=wedge(3.6,2.6,1.0,(0,0,1.95),roofm,'roof',bev=0.04)
    ridge=rbox(3.7,0.12,0.12,(0,0,2.9),trim,'ridge',bev=0.03)
    door=rbox(1.2,0.10,1.35,(0,-1.12,0.78),trim,'door',bev=0.03)
    dpanel=rbox(1.0,0.06,1.15,(0,-1.16,0.78),mat('dirt_dk'),'dp',bev=0.02)
    w1=_window(0.5,0.6,(-1.0,-1.12,1.35)); w2=_window(0.5,0.6,(1.0,-1.12,1.35))
    # ore bays
    parts=[base,walls,roof,ridge,door,dpanel,w1,w2]
    fx=2.35
    parts.append(rbox(1.5,0.12,0.6,(fx,-0.95,0.3),trim,'bw',bev=0.02))
    parts.append(rbox(0.12,2.0,0.6,(fx-0.7,0,0.3),trim,'bw2',bev=0.02))
    parts.append(rbox(0.12,2.0,0.6,(fx+0.7,0,0.3),trim,'bw3',bev=0.02))
    parts.append(box(1.4,2.0,0.08,(fx,0,0.06),mat('dirt_dk'),'bf'))
    for p in (-0.35,0.0,0.35):  # posts
        parts.append(rbox(0.12,0.12,0.7,(fx-0.7,p*2,0.35),trim,'pst',bev=0.02))
    return join(parts,'x')

@asset('Building','Refinery')
def c2():
    st=mat('steel'); stk=mat('steel_dk'); acc=mat('orange')
    base=rbox(3.2,2.6,0.25,(0,0,0.12),mat('concrete_dk'),'found',bev=0.03)
    body=rbox(3.0,2.4,2.0,(0,0,1.2),st,'body',bev=0.06)
    roof=rbox(3.05,2.45,0.2,(0,0,2.3),stk,'roof',bev=0.03)
    stripe=rbox(3.02,2.42,0.28,(0,0,0.55),acc,'stripe',bev=0.02)
    chim=rcyl(0.42,2.4,12,(0.85,0.6,3.2),m=st,name='chim')
    ctop=rcyl(0.5,0.35,12,(0.85,0.6,4.45),m=stk,name='ctop')
    tank=rcyl(0.55,1.0,12,(-0.9,0.7,2.9),'Y',st,'tank')
    tcap1=rcyl(0.55,0.06,12,(-0.9,0.2,2.9),'Y',stk,'tc'); tcap2=rcyl(0.55,0.06,12,(-0.9,1.2,2.9),'Y',stk,'tc2')
    pipes=[rcyl(0.12,2.0,8,(-1.35,-0.5,1.1),m=stk,name='pipe'),
           rcyl(0.12,1.4,8,(-1.35,0.3,0.8),m=stk,name='pipe2')]
    elbow=rcyl(0.12,0.5,8,(-1.35,-0.5,2.05),'Y',stk,'elb')
    conv=wedge(0.7,1.0,0.4,(0,-1.6,0.5),stk,'conv',bev=0.02); conv.rotation_euler=(0,math.radians(-90),math.radians(90))
    haz=[rbox(0.28,0.02,0.28,(-1.4+i*0.35,-1.31,0.55),mat('dark') if i%2 else acc,'h',bev=0.0) for i in range(9)]
    win=_window(0.5,0.6,(-0.6,-1.21,1.3))
    door=rbox(0.9,0.1,1.3,(0.6,-1.21,0.75),stk,'door',bev=0.03)
    return join([base,body,roof,stripe,chim,ctop,tank,tcap1,tcap2,elbow,conv,win,door]+pipes+haz,'x')

@asset('Building','Market')
def c3():
    wall=mat('gold_warm'); roofm=mat('red_roof'); trim=mat('wood_dk')
    base=rbox(2.8,2.4,0.22,(0,0,0.11),mat('concrete_dk'),'found',bev=0.03)
    walls=rbox(2.6,2.2,1.5,(0,0,0.95),wall,'walls',bev=0.05)
    roof=wedge(3.0,2.6,1.05,(0,0,1.7),roofm,'roof',bev=0.04)
    ridge=rbox(3.1,0.12,0.12,(0,0,2.72),mat('red_dk'),'ridge',bev=0.03)
    # striped awning (alternating slats)
    awn=[]
    for i in range(7):
        c=roofm if i%2 else mat('snow')
        s=rbox(0.34,0.85,0.06,(-1.05+i*0.35,-1.35,1.2),c,'aw',bev=0.01)
        s.rotation_euler=(math.radians(20),0,0); awn.append(s)
    counter=rbox(2.1,0.35,0.75,(0,-1.15,0.4),mat('wood'),'counter',bev=0.03)
    ctop=rbox(2.2,0.45,0.08,(0,-1.15,0.8),trim,'ctop',bev=0.02)
    post=rbox(0.12,0.12,1.8,(1.55,-1.5,0.9),trim,'post',bev=0.02)
    sign=rbox(0.95,0.1,0.55,(1.55,-1.5,1.6),mat('wood'),'sign',bev=0.03)
    signtxt=rbox(0.7,0.06,0.3,(1.55,-1.44,1.6),mat('gold'),'st',bev=0.01)
    crates=[rbox(0.42,0.42,0.42,(-1.45,-1.45,0.22),mat('wood'),'cr',bev=0.03),
            rbox(0.34,0.34,0.34,(-1.05,-1.55,0.18),mat('wood_dk'),'cr2',bev=0.03)]
    win=_window(0.55,0.6,(0.8,-1.11,1.05))
    return join([base,walls,roof,ridge,counter,ctop,post,sign,signtxt,win]+awn+crates,'x')

@asset('Building','Station')
def c4():
    stone=mat('concrete'); trim=mat('wood_dk'); roofm=mat('red_roof')
    plat=rbox(4.0,1.7,0.5,(0,0.1,0.25),stone,'plat',bev=0.04)
    edge=rbox(4.05,0.12,0.12,(0,-0.72,0.5),trim,'edge',bev=0.02)
    posts=[rbox(0.14,0.14,1.5,(x,0.5,1.2),trim,'p',bev=0.02) for x in (-1.5,-0.5,0.5,1.5)]
    roof=wedge(3.5,1.5,0.65,(0,0.5,1.95),roofm,'roof',bev=0.04)
    ridge=rbox(3.6,0.1,0.1,(0,0.5,2.6),mat('red_dk'),'ridge',bev=0.02)
    board=rbox(1.3,0.1,0.5,(0,-0.35,1.35),mat('wood'),'board',bev=0.03)
    boardtxt=rbox(1.0,0.06,0.28,(0,-0.42,1.35),mat('snow'),'bt',bev=0.01)
    bench=rbox(1.0,0.3,0.12,(1.0,0.3,0.75),trim,'bench',bev=0.02)
    lamp=join([rcyl(0.06,1.1,8,(-1.5,-0.4,1.05),m=mat('steel_dk'),name='lp'),
               ico(0.14,1,(-1.5,-0.4,1.65),emat('gold_warm',1.5),'lh')],'lamp')
    chute=rbox(0.8,0.7,0.1,(1.3,-0.55,1.0),mat('steel'),'chute',bev=0.03); chute.rotation_euler=(math.radians(32),0,0)
    return join([plat,edge,roof,ridge,board,boardtxt,bench,lamp,chute]+posts,'x')

@asset('Building','LoadingDock')
def c5():
    con=mat('concrete'); cond=mat('concrete_dk'); st=mat('steel')
    bay=rbox(2.2,2.0,0.7,(0,0,0.35),con,'bay',bev=0.04)
    lip=rbox(2.25,0.15,0.15,(0,-1.0,0.7),cond,'lip',bev=0.02)
    ramp=wedge(1.5,1.0,0.7,(0,-1.45,0),cond,'ramp',bev=0.03); ramp.rotation_euler=(0,0,math.radians(90))
    boll=[rcyl(0.12,0.65,8,(x,-0.9,0.32),m=mat('gold_warm'),name='b') for x in (-0.85,0.85)]
    for b in boll: setmat(b,mat('gold_warm'))
    frame=[rbox(0.16,0.16,1.9,(x,0.7,0.95),st,'fr',bev=0.02) for x in (-0.7,0.7)]
    beam=rbox(1.7,0.16,0.16,(0,0.7,1.85),st,'beam',bev=0.02)
    hopper=cone(0.6,0.75,8,(0,0.5,1.4),st,'hop'); hopper.rotation_euler=(math.radians(180),0,0)
    rail=[rbox(1.7,0.06,0.06,(0,0.6,1.0),st,'r',bev=0.01)]
    return join([bay,lip,ramp,beam,hopper]+boll+frame+rail,'x')

@asset('Building','TierKit','base',is_set=True)
def c6():
    st=mat('steel'); stk=mat('steel_dk'); wd=mat('wood_dk'); go=mat('gold_warm')
    chimney=join([rcyl(0.3,1.7,10,(0,0,0.85),m=st,name='ch'),rcyl(0.36,0.22,10,(0,0,1.8),m=stk,name='ct')],'SM_addon_chimney'); chimney.location=(-2.6,0,0)
    wing=rbox(1.4,1.2,1.0,(0,0,0.5),mat('dirt'),'SM_addon_wing'); wing.location=(-0.7,0,0)
    sign=join([rbox(1.5,0.12,0.7,(0,0,1.1),go,'sg',bev=0.03),
               rbox(0.1,0.1,1.1,(-0.6,0,0.55),wd,'p1',bev=0.02),
               rbox(0.1,0.1,1.1,(0.6,0,0.55),wd,'p2',bev=0.02)],'SM_addon_sign'); sign.location=(1.1,0,0)
    tank=join([rcyl(0.5,0.9,12,(0,0,0.5),'Y',st,'tk'),rcyl(0.5,0.06,12,(0,-0.45,0.5),'Y',stk,'te')],'SM_addon_tank'); tank.location=(2.8,0,0)
    lights=rbox(1.3,0.05,0.05,(0,0,0.9),emat('gold_warm',1.5),'SM_addon_lights'); lights.location=(4.2,0,0)
    return [chimney,wing,sign,tank,lights]

# ---- E. Rail & Road ----
def _rail_pieces():
    parts=[box(0.62,1.9,0.14,(0,0,0.07),mat('gravel'),'bed')]
    displace(parts[0],0.015,seed=40,zmin=0.07)
    for t in [-0.72,-0.36,0,0.36,0.72]:
        parts.append(box(0.58,0.16,0.10,(0,t,0.17),mat('wood'),'tie'))
    for x in (-0.24,0.24):
        parts.append(box(0.06,1.9,0.11,(x,0,0.24),mat('steel'),'rail'))
    return parts
@asset('Rail','Straight','center')
def e1(): return join(_rail_pieces(),'x')
@asset('Rail','Curve','center')
def e2():
    R=1.0; cxp,cyp=R,-R; parts=[]; n=6
    for i in range(n+1):
        ang=math.radians(90)*(i/n)
        px=cxp-R*math.cos(ang); py=cyp+R*math.sin(ang)
        t=box(0.58,0.16,0.10,(px,py,0.17),mat('wood'),'tie'); t.rotation_euler=(0,0,ang); parts.append(t)
    for i in range(n):
        ang=math.radians(90)*((i+0.5)/n)
        px=cxp-R*math.cos(ang); py=cyp+R*math.sin(ang)
        b=box(0.62,0.42,0.14,(px,py,0.07),mat('gravel'),'bed'); b.rotation_euler=(0,0,ang); parts.append(b)
    for rr in (R-0.24,R+0.24):
        for i in range(n):
            ang=math.radians(90)*((i+0.5)/n)
            px=cxp-rr*math.cos(ang); py=cyp+rr*math.sin(ang)
            s=box(0.06,(math.pi/2*rr)/n+0.05,0.11,(px,py,0.24),mat('steel'),'r'); s.rotation_euler=(0,0,ang); parts.append(s)
    return join(parts,'x')
@asset('Rail','Junction','center')
def e3():
    parts=_rail_pieces()
    parts.append(box(0.9,0.62,0.14,(0.75,0,0.07),mat('gravel'),'bbed'))
    for y in (-0.24,0.24): parts.append(box(0.9,0.06,0.11,(0.75,y,0.24),mat('steel'),'br'))
    lever=join([rcyl(0.05,0.5,8,(0.2,0.7,0.28),m=mat('steel_dk'),name='lv'),ico(0.08,1,(0.2,0.7,0.55),mat('red_roof'),'kn')],'lever')
    parts.append(lever)
    return join(parts,'x')
@asset('Rail','Buffer','center')
def e4():
    parts=_rail_pieces()
    parts.append(rbox(0.7,0.35,0.55,(0,0.85,0.32),mat('steel_dk'),'stop',bev=0.03))
    for x in (-0.24,0.24): parts.append(rcyl(0.08,0.25,8,(x,0.62,0.4),'Y',mat('red_roof'),'buf'))
    return join(parts,'x')
@asset('Road','Straight','center')
def e5():
    surf=box(2.0,2.0,0.10,(0,0,0.05),mat('dirt'),'surf')
    displace(surf,0.012,seed=44,zmin=0.05)
    ruts=[rbox(0.22,2.0,0.02,(x,0,0.10),mat('dirt_dk'),'rut',bev=0.0) for x in (-0.45,0.45)]
    stones=[ico(0.06,1,(random.Random(i).uniform(-0.8,0.8),random.Random(i+9).uniform(-0.8,0.8),0.11),mat('gravel'),'s') for i in range(4)]
    return join([surf]+ruts+stones,'x')
@asset('Road','Curve','center')
def e6():
    parts=[]; R=1.0; cxp,cyp=R,-R; n=7
    for i in range(n):
        ang=math.radians(90)*((i+0.5)/n)
        px=cxp-R*math.cos(ang); py=cyp+R*math.sin(ang)
        b=box(0.95,0.44,0.10,(px,py,0.05),mat('dirt'),'seg'); b.rotation_euler=(0,0,ang); parts.append(b)
    return join(parts,'x')

# =====================================================================
#  BUILDERS (Part B: vehicles, ore, products, currency, dressing, character)
# =====================================================================
def _wheel(r,w,loc,name,seg=8):
    tire=rcyl(r,w,seg,loc,'X',mat('dark'),name,rim=0)
    hub=rcyl(r*0.42,w*1.2,6,loc,'X',mat('steel_dk'),name+'h',rim=0)
    return join([tire,hub],name)
def _vwin(tx,ty,tz,loc,m=None):
    return box(tx,ty,tz,loc,m or mat('glass'),'win')

# ---- D. Vehicles ----
@asset('Vehicle','TrainEngine','base')
def d1():
    body=mat('train_green'); dk=mat('train_green_dk'); trim=mat('dark'); st=mat('steel_dk')
    chassis=box(1.25,2.5,0.24,(0,-0.1,0.34),trim,'chassis')
    boiler=rcyl(0.58,1.5,12,(0,-0.55,0.9),'Y',body,'boiler')
    band1=rcyl(0.61,0.09,10,(0,-0.2,0.9),'Y',dk,'band'); band2=rcyl(0.61,0.09,10,(0,-1.0,0.9),'Y',dk,'band2')
    smokebox=rcyl(0.6,0.16,12,(0,-1.35,0.9),'Y',trim,'sb')
    cab=rbox(1.15,0.95,1.15,(0,0.9,1.0),body,'cab',bev=0.05)
    cabroof=box(1.26,1.06,0.14,(0,0.9,1.66),dk,'roof')
    winL=_vwin(0.06,0.42,0.46,(-0.6,0.62,1.12),dk); winR=_vwin(0.06,0.42,0.46,(0.6,0.62,1.12),dk)
    chim=rcyl(0.16,0.5,8,(0,-1.0,1.5),m=trim,name='chim'); chimcap=rcyl(0.22,0.12,8,(0,-1.0,1.78),m=trim,name='cc')
    dome=rcyl(0.2,0.22,8,(0,-0.35,1.55),m=st,name='dm')
    head=rcyl(0.11,0.1,8,(0,-1.46,0.9),'Y',mat('gold_warm'),'head')
    cc=[]
    for i,x in enumerate((-0.24,0.0,0.24)):
        s=box(0.05,0.45,0.45,(x,-1.6,0.28),trim,'cc%d'%i); s.rotation_euler=(math.radians(38),0,0); cc.append(s)
    wheels=[_wheel(0.33,0.15,(0.62,y,0.35),'w%d'%i) for i,y in enumerate((-0.75,0.55))]
    wheels+=[_wheel(0.33,0.15,(-0.62,y,0.35),'wl%d'%i) for i,y in enumerate((-0.75,0.55))]
    buffers=[rcyl(0.08,0.16,6,(x,1.4,0.55),'Y',st,'buf') for x in (-0.34,0.34)]
    return join([chassis,boiler,band1,band2,smokebox,cab,cabroof,winL,winR,chim,chimcap,dome,head]+cc+wheels+buffers,'x')

@asset('Vehicle','OreWagon','base')
def d2():
    body=mat('steel'); dk=mat('steel_dk'); trim=mat('dark')
    chassis=rbox(1.2,1.9,0.22,(0,0,0.32),trim,'chassis',bev=0.03)
    floor=rbox(1.35,1.7,0.14,(0,0,0.5),body,'floor',bev=0.03)
    walls=[]
    walls.append(box(0.12,1.7,0.7,(-0.72,0,0.82),body,'wl'))
    walls.append(box(0.12,1.7,0.7,(0.72,0,0.82),body,'wr'))
    walls.append(box(1.5,0.12,0.7,(0,-0.85,0.82),body,'wf'))
    walls.append(box(1.5,0.12,0.7,(0,0.85,0.82),body,'wb'))
    for w in walls[:2]: w.rotation_euler=(0,math.radians(9 if w.location.x<0 else -9),0)
    ribs=[rbox(0.05,0.1,0.7,(x,0,0.82),dk,'rib',bev=0.0) for x in (-0.73,0.73)]
    couplers=[rbox(0.18,0.3,0.18,(0,y,0.42),trim,'cp',bev=0.02) for y in (-1.0,1.0)]
    wheels=[_wheel(0.3,0.14,(0.58,y,0.3),'w%d'%i) for i,y in enumerate((-0.6,0.6))]
    wheels+=[_wheel(0.3,0.14,(-0.58,y,0.3),'wl%d'%i) for i,y in enumerate((-0.6,0.6))]
    return join([chassis,floor]+walls+ribs+couplers+wheels,'x')

@asset('Vehicle','OreTruck','base')
def d3():
    cabm=mat('truck_blue'); dk=mat('blue_dk'); bed=mat('steel_dk'); trim=mat('dark')
    chassis=rbox(1.0,2.2,0.2,(0,0,0.32),trim,'chassis',bev=0.02)
    cab=rbox(0.95,0.85,0.85,(0,-0.62,0.82),cabm,'cab',bev=0.05)
    hood=rbox(0.95,0.55,0.5,(0,-1.2,0.58),cabm,'hood',bev=0.05)
    winds=box(0.8,0.06,0.45,(0,-0.9,1.0),mat('glass'),'ws')
    winL=_vwin(0.06,0.35,0.4,(-0.49,-0.55,0.9),dk); winR=_vwin(0.06,0.35,0.4,(0.49,-0.55,0.9),dk)
    grille=rbox(0.8,0.06,0.3,(0,-1.48,0.55),dk,'grille',bev=0.02)
    heads=[rcyl(0.07,0.06,8,(x,-1.48,0.62),'Y',mat('gold_warm'),'hl') for x in (-0.3,0.3)]
    bumper=rbox(0.95,0.12,0.14,(0,-1.5,0.35),trim,'bmp',bev=0.03)
    bedbox=rbox(0.95,1.0,0.5,(0,0.55,0.7),bed,'bed',bev=0.04)
    for v in bedbox.data.vertices:                     # hollow tipper
        if v.co.z>0.12 and abs(v.co.x)<0.4 and abs(v.co.y)<0.42: v.co.z-=0.28
    wheels=[_wheel(0.28,0.16,(0.5,y,0.3),'w%d'%i) for i,y in enumerate((-0.65,0.6))]
    wheels+=[_wheel(0.28,0.16,(-0.5,y,0.3),'wl%d'%i) for i,y in enumerate((-0.65,0.6))]
    return join([chassis,cab,hood,winds,winL,winR,grille,bumper,bedbox]+heads+wheels,'x')

@asset('Vehicle','CargoTruck','base')
def d4():
    cabm=mat('orange'); dk=mat('red_dk'); wood=mat('wood'); trim=mat('dark')
    chassis=rbox(1.0,2.4,0.2,(0,0,0.32),trim,'chassis',bev=0.02)
    cab=rbox(0.95,0.85,0.9,(0,-0.8,0.85),cabm,'cab',bev=0.05)
    hood=rbox(0.95,0.5,0.5,(0,-1.32,0.58),cabm,'hood',bev=0.05)
    winds=box(0.8,0.06,0.45,(0,-1.05,1.05),mat('glass'),'ws')
    winL=_vwin(0.06,0.35,0.4,(-0.49,-0.72,0.95),dk); winR=_vwin(0.06,0.35,0.4,(0.49,-0.72,0.95),dk)
    grille=rbox(0.8,0.06,0.3,(0,-1.58,0.55),dk,'grille',bev=0.02)
    heads=[rcyl(0.07,0.06,8,(x,-1.58,0.62),'Y',mat('gold_warm'),'hl') for x in (-0.3,0.3)]
    bumper=rbox(0.95,0.12,0.14,(0,-1.6,0.35),trim,'bmp',bev=0.03)
    flat=rbox(0.98,1.4,0.12,(0,0.5,0.62),wood,'flat',bev=0.03)
    rails=[box(0.07,1.4,0.28,(-0.46,0.5,0.82),wood,'rl'),
           box(0.07,1.4,0.28,(0.46,0.5,0.82),wood,'rr'),
           box(0.98,0.07,0.28,(0,1.18,0.82),wood,'rb')]
    wheels=[_wheel(0.28,0.16,(0.5,y,0.3),'w%d'%i) for i,y in enumerate((-0.75,0.6))]
    wheels+=[_wheel(0.28,0.16,(-0.5,y,0.3),'wl%d'%i) for i,y in enumerate((-0.75,0.6))]
    return join([chassis,cab,hood,winds,winL,winR,grille,bumper,flat]+rails+heads+wheels,'x')

# ---- F. Ore ----
@asset('Ore','Chunk','center')
def f1():
    rock=ico(0.24,1,(0,0,0.2),mat('iron'),'rock'); displace(rock,0.05,seed=31)
    fac=mat('copper',rough=0.35); facs=[]
    random.seed(33)
    for i in range(3):
        f=rbox(0.13,0.13,0.06,(random.uniform(-0.09,0.09),random.uniform(-0.09,0.09),0.30),fac,'f%d'%i,bev=0.0)
        f.rotation_euler=(random.uniform(0,0.4),random.uniform(0,0.4),random.uniform(0,1)); facs.append(f)
    return join([rock]+facs,'x')

@asset('Ore','GemCluster','center')
def f2():
    base=ico(0.22,1,(0,0,0.1),mat('rock'),'base');
    for v in base.data.vertices: v.co.z*=0.6
    displace(base,0.04,seed=41)
    cm=mat('ruby',rough=0.2); shards=[]; random.seed(43)
    for i in range(5):
        h=random.uniform(0.3,0.6)
        s=cone(0.09,h,6,(random.uniform(-0.13,0.13),random.uniform(-0.13,0.13),0.12+h/2),cm,'s%d'%i)
        s.rotation_euler=(random.uniform(-0.25,0.25),random.uniform(-0.25,0.25),random.uniform(0,1)); shards.append(s)
    return join([base]+shards,'x')

def _pile(scale,seed):
    heap=ico(0.5*scale,1,(0,0,0),mat('iron'),'heap')
    for v in heap.data.vertices: v.co.z=(v.co.z*0.5+0.5)*0.55*scale
    displace(heap,0.06*scale,seed=seed,zmin=0.02)
    chunks=[]; random.seed(seed)
    for i in range(3):
        c=ico(0.13*scale,1,(random.uniform(-0.25,0.25)*scale,random.uniform(-0.25,0.25)*scale,0.28*scale),mat('iron'),'c%d'%i)
        displace(c,0.03*scale,seed=seed+i); chunks.append(c)
    return join([heap]+chunks,'x')
@asset('Ore','PileSmall')
def f3s(): return _pile(0.7,51)
@asset('Ore','PileMed')
def f3m(): return _pile(1.0,52)
@asset('Ore','PileLarge')
def f3l(): return _pile(1.4,53)

# ---- G. Products ----
@asset('Product','Coke')
def g1():
    dk=mat('coal',rough=0.5); parts=[]
    lay=[(-0.2,-0.2,0.11),(0.0,-0.2,0.11),(0.2,-0.2,0.11),(-0.1,0.0,0.30),(0.1,0.0,0.30),(0,0.16,0.49)]
    for x,y,z in lay: parts.append(box(0.19,0.19,0.19,(x,y,z),dk,'b'))
    return join(parts,'x')
@asset('Product','MetalBar')
def g2():
    bar=rbox(0.5,0.24,0.16,(0,0,0.08),mat('gold',metallic=0.5,rough=0.3),'bar',bev=0.03)
    for v in bar.data.vertices:
        if v.co.z>0.08: v.co.x*=0.82; v.co.y*=0.82
    return bar
@asset('Product','SteelBeam','center')
def g3():
    st=mat('steel',metallic=0.4,rough=0.35)
    top=rbox(0.4,1.6,0.09,(0,0,0.34),st,'t',bev=0.02); bot=rbox(0.4,1.6,0.09,(0,0,-0.34),st,'b',bev=0.02); web=box(0.1,1.6,0.68,(0,0,0),st,'w')
    return join([top,bot,web],'x')
def _gem(r,ht,hb,seg,m,name):
    up=cone(r,ht,seg,(0,0,hb),m,name+'u')
    dn=cone(r,hb,seg,(0,0,0),m,name+'d'); dn.rotation_euler=(math.radians(180),0,0)
    return join([up,dn],name)
@asset('Product','CutGem')
def g4():
    gm=mat('emerald',rough=0.15)
    crown=rcyl(0.22,0.14,8,(0,0,0.28),m=gm,name='cr',r2=0.28,rim=0)
    pav=cone(0.28,0.32,8,(0,0,0.16),gm,'pav'); pav.rotation_euler=(math.radians(180),0,0)
    return join([crown,pav],'x')
@asset('Product','Diamond')
def g5():
    gm=mat('diamond',rough=0.1)
    table=rcyl(0.18,0.05,8,(0,0,0.32),m=gm,name='t',r2=0.2,rim=0)
    crown=rcyl(0.22,0.1,8,(0,0,0.24),m=gm,name='cr',r2=0.26,rim=0)
    pav=cone(0.26,0.3,8,(0,0,0.14),gm,'pav'); pav.rotation_euler=(math.radians(180),0,0)
    return join([table,crown,pav],'x')
@asset('Product','RubyRing')
def g6():
    go=mat('gold',metallic=0.6,rough=0.25)
    band=rcyl(0.24,0.09,12,(0,0,0.09),m=go,name='band',r2=0.24,rim=0)
    hole=rcyl(0.15,0.12,12,(0,0,0.09),m=mat('dark'),name='hole',rim=0)
    setting=rcyl(0.14,0.1,8,(0,0,0.2),m=go,name='set')
    gem=_gem(0.13,0.14,0.08,6,mat('ruby',rough=0.15),'gem'); gem.location=(0,0,0.32)
    return join([band,hole,setting,gem],'x')
@asset('Product','Crown')
def g7():
    go=mat('gold',metallic=0.6,rough=0.25)
    ring=rcyl(0.45,0.32,12,(0,0,0.16),m=go,name='ring',r2=0.45,rim=0)
    peaks=[]
    for i in range(6):
        a=i*math.radians(60)
        peaks.append(cone(0.1,0.36,4,(math.cos(a)*0.42,math.sin(a)*0.42,0.46),go,'pk%d'%i))
    gems=[]
    for i in range(3):
        a=i*math.radians(120)+math.radians(30)
        gems.append(ico(0.055,0,(math.cos(a)*0.45,math.sin(a)*0.45,0.28),mat('ruby',rough=0.15),'g%d'%i))
    dia=_gem(0.1,0.12,0.08,6,mat('diamond',rough=0.1),'dia'); dia.location=(0,-0.46,0.34)
    return join([ring]+peaks+gems+[dia],'x')
@asset('Product','Crate')
def g8():
    wd=mat('wood'); body=rbox(0.6,0.6,0.6,(0,0,0.3),wd,'body',bev=0.03)
    band=box(0.62,0.62,0.16,(0,0,0.35),mat('orange'),'band')
    corners=[box(0.08,0.08,0.62,(sx*0.28,sy*0.28,0.3),mat('steel_dk'),'cn') for sx in (-1,1) for sy in (-1,1)]
    slats=[box(0.6,0.02,0.05,(0,0,z),mat('wood_dk'),'sl') for z in (0.12,0.48)]
    return join([body,band]+corners+slats,'x')

# ---- H. Currency ----
@asset('Currency','Coin','center')
def h1():
    coin=rcyl(0.3,0.09,10,(0,0,0),m=mat('coin',metallic=0.5,rough=0.3),name='coin',rim=0.0)
    star=cone(0.12,0.04,5,(0,0,0.055),mat('gold_warm_dk'),'star')
    return join([coin,star],'x')
@asset('Currency','PremiumGem','center')
def h2():
    gm=mat('gem',rough=0.15)
    crown=rcyl(0.16,0.1,6,(0,0,0.16),m=gm,name='cr',r2=0.22,rim=0)
    pav=cone(0.24,0.3,6,(0,0,0.02),gm,'p'); pav.rotation_euler=(math.radians(180),0,0)
    tbl=rcyl(0.13,0.04,6,(0,0,0.23),m=gm,name='t',rim=0)
    return join([crown,pav,tbl],'x')
@asset('Currency','MoneyBag')
def h3():
    dirt=mat('dirt')
    bag=ico(0.4,1,(0,0,0.32),dirt,'bag')
    for v in bag.data.vertices:
        v.co.z=(v.co.z*0.5+0.5)*0.8
        if v.co.z>0.55: v.co.x*=0.45; v.co.y*=0.45
    displace(bag,0.02,seed=61,zmin=0.05)
    neck=rcyl(0.15,0.16,8,(0,0,0.78),m=mat('wood'),name='neck',rim=0)
    sym=cone(0.12,0.04,5,(0,-0.3,0.4),mat('coin',metallic=0.5),'sym'); sym.rotation_euler=(math.radians(90),0,0)
    coins=[rcyl(0.12,0.05,8,(0.35,0.1,0.05),m=mat('coin',metallic=0.5),name='c1',rim=0),
           rcyl(0.12,0.05,8,(-0.28,-0.22,0.05),m=mat('coin',metallic=0.5),name='c2',rim=0)]
    return join([bag,neck,sym]+coins,'x')

# ---- I. Set dressing ----
@asset('Dressing','PineTree')
def i1():
    trunk=rcyl(0.12,0.5,8,(0,0,0.25),m=mat('wood'),name='trunk',rim=0.02)
    tiers=[]
    for i,(r,h,z) in enumerate([(0.72,0.8,0.65),(0.56,0.72,1.15),(0.4,0.62,1.65)]):
        c=cone(r,h,8,(0,0,z),mat('grass_dk'),'c%d'%i); displace(c,0.04,seed=70+i,zmin=z-h/2+0.05); tiers.append(c)
    return join([trunk]+tiers,'x')
@asset('Dressing','Boulders','base',is_set=True)
def i2():
    out=[]
    for i,(sc,x) in enumerate([(0.35,-0.9),(0.55,0),(0.8,1.3)]):
        b=ico(sc,1,(x,0,sc*0.55),mat('rock'),'SM_boulder_%d'%i)
        for v in b.data.vertices: v.co.z*=0.8
        displace(b,sc*0.16,seed=60+i,zmin=0.02); out.append(b)
    return out
@asset('Dressing','Bush')
def i3():
    parts=[]; random.seed(70)
    for i in range(4):
        r=random.uniform(0.22,0.34)
        s=ico(r,1,(random.uniform(-0.2,0.2),random.uniform(-0.2,0.2),random.uniform(0.18,0.34)),mat('grass_dk'),'b%d'%i)
        displace(s,r*0.18,seed=71+i); parts.append(s)
    return join(parts,'x')
@asset('Dressing','Cloud','center')
def i4():
    parts=[]
    for x,y,r in [(-0.6,0,0.42),(0,0.1,0.58),(0.6,0,0.44),(0.1,-0.22,0.4)]:
        s=ico(r,1,(x,y,0),mat('snow'),'p');
        for v in s.data.vertices: v.co.z*=0.75
        displace(s,r*0.1,seed=81); parts.append(s)
    return join(parts,'x')
@asset('Dressing','PropsKit','base',is_set=True)
def i5():
    wd=mat('wood'); wdk=mat('wood_dk'); st=mat('steel_dk'); go=mat('gold_warm')
    fence=join([rbox(0.1,0.08,0.6,(0,-0.5,0.3),wd,'fp1',bev=0.02),rbox(0.1,0.08,0.6,(0,0.5,0.3),wd,'fp2',bev=0.02),
                rbox(0.06,1.1,0.08,(0,0,0.45),wdk,'fr1',bev=0.01),rbox(0.06,1.1,0.08,(0,0,0.2),wdk,'fr2',bev=0.01)],'SM_prop_fence'); fence.location=(-1.8,0,0)
    signpost=join([rbox(0.1,0.1,1.0,(0,0,0.5),wd,'pole',bev=0.02),rbox(0.55,0.08,0.28,(0.25,0,0.85),go,'arrow',bev=0.02)],'SM_prop_signpost'); signpost.location=(-0.6,0,0)
    lamp=join([rcyl(0.07,1.1,8,(0,0,0.55),m=st,name='pole',rim=0.02),rbox(0.24,0.24,0.24,(0,0,1.2),st,'box',bev=0.03),ico(0.13,1,(0,0,1.2),emat('gold_warm',1.4),'glow')],'SM_prop_lamp'); lamp.location=(0.6,0,0)
    barrel=rcyl(0.28,0.62,12,(0,0,0.31),m=wd,name='SM_prop_barrel',rim=0.03)
    band1=rcyl(0.29,0.05,12,(0,0,0.15),m=wdk,name='bb1',r2=0.29,rim=0); band2=rcyl(0.29,0.05,12,(0,0,0.47),m=wdk,name='bb2',r2=0.29,rim=0)
    barrel=join([barrel,band1,band2],'SM_prop_barrel'); barrel.location=(1.8,0,0)
    return [fence,signpost,lamp,barrel]

# ---- J. Character ----
@asset('Character','Miner','base')
def j1():
    cloth=mat('truck_blue'); clothd=mat('blue_dk'); skin=mat('skin'); hat=mat('gold_warm'); boot=mat('wood_dk')
    torso=rbox(0.5,0.32,0.66,(0,0,0.9),cloth,'torso',bev=0.06)
    strap1=box(0.08,0.33,0.5,(-0.15,0,0.95),clothd,'st1'); strap2=box(0.08,0.33,0.5,(0.15,0,0.95),clothd,'st2')
    head=rbox(0.32,0.30,0.32,(0,0,1.42),skin,'head',bev=0.07)
    hatb=rcyl(0.22,0.16,12,(0,0,1.64),m=hat,name='hat',rim=0.02)
    brim=rcyl(0.3,0.05,12,(0,-0.04,1.57),m=hat,name='brim',r2=0.3,rim=0.01)
    lamp=rcyl(0.06,0.06,8,(0,-0.2,1.68),'Y',emat('gold_warm',1.5),'lamp')
    arms=[rbox(0.14,0.14,0.6,(-0.33,0,0.98),cloth,'la',bev=0.04),rbox(0.14,0.14,0.6,(0.33,0,0.98),cloth,'ra',bev=0.04)]
    hands=[ico(0.09,1,(-0.33,0,0.66),skin,'lh'),ico(0.09,1,(0.33,0,0.66),skin,'rh')]
    legs=[rbox(0.18,0.2,0.5,(-0.13,0,0.3),clothd,'ll',bev=0.03),rbox(0.18,0.2,0.5,(0.13,0,0.3),clothd,'rl',bev=0.03)]
    boots=[rbox(0.2,0.28,0.16,(-0.13,-0.04,0.08),boot,'lb',bev=0.03),rbox(0.2,0.28,0.16,(0.13,-0.04,0.08),boot,'rb',bev=0.03)]
    return join([torso,strap1,strap2,head,hatb,brim,lamp]+arms+hands+legs+boots,'x')

# =====================================================================
def purge():
    _mats.clear()
    for _ in range(4):
        try: bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
        except Exception: break

def run(names):
    lines=[]
    for nm in names:
        if nm not in REG: lines.append('%s: NOT REGISTERED'%nm); continue
        cat,name,fn,origin,is_set=REG[nm]
        try:
            new_scene(); res=fn(); sm='SM_%s_%s'%(cat,name)
            if is_set: path,tris=finalize_set(res,sm,origin)
            else: path,tris=finalize(res,sm,origin)
            lines.append('OK  %-28s tris=%-5d'%(sm,tris))
        except Exception:
            lines.append('ERR %s: %s'%(nm, traceback.format_exc().splitlines()[-1]))
    rep='\n'.join(lines)
    with open(REPORT,'w') as f: f.write(rep)
    print(rep); return rep
