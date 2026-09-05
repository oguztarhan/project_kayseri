"""Editable 3D reconstruction of the supplied Focus Ladder image.

Run with Blender --background --python this_file. Nothing under Assets is changed.
"""
import bpy, sys, math, random, json
from pathlib import Path
from mathutils import Vector
from math import sin, cos, pi

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'design' / 'focus-ladder-blender'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / 'Tools' / 'blender' / 'isomap'))
from lib import B, mat, bevel
import parts

R = random.Random(1921)
K = math.sqrt(.5)
def W(s,t,z=0): return Vector(((t+s*1.3459)*K,(t-s*1.3459)*K,z))
def col(name):
    c=bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c); return c
def mesh(name,verts,faces,mats,C):
    me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
    ob=bpy.data.objects.new(name,me); C.objects.link(ob)
    for m in mats: me.materials.append(bpy.data.materials[m])
    return ob
def finish(b,name,C,s=0,t=0,z=0,width=.06):
    ob=b.make(name,collection=C,loc=W(s,t,z))
    if width: bevel(ob,width,2)
    return ob
def line(name,pts,r,m,C,cyclic=False):
    cu=bpy.data.curves.new(name,'CURVE'); cu.dimensions='3D'; cu.resolution_u=2
    sp=cu.splines.new('POLY'); sp.points.add(len(pts)-1)
    for p,co in zip(sp.points,pts): p.co=(*co,1)
    sp.use_cyclic_u=cyclic; cu.bevel_depth=r; cu.bevel_resolution=2
    ob=bpy.data.objects.new(name,cu); C.objects.link(ob); cu.materials.append(bpy.data.materials[m]); return ob
def box(b,sz,p,m): b.use(m).box(sz,p)
def cylinder(b,r,h,p,m,rot=(0,0,0),seg=20): b.use(m).cyl(r,h,p,rot,seg)
def pipe(b,pts,r=.4,m='chrome'): b.use(m).tube(r,pts,12)
def disk_ring(b,r,z,x,y,m='chrome',th=.12):
    pts=[(x+r*cos(i*pi/32),y+r*sin(i*pi/32),z) for i in range(65)]
    pipe(b,pts,th,m)

def palette():
    colors={
      'blue':(.018,.24,.66),'blue_light':(.065,.43,.86),'blue_dark':(.025,.105,.28),
      'yellow':(.98,.56,.015),'orange':(.99,.43,.01),'white':(.77,.84,.84),
      'offwhite':(.6,.66,.65),'steel':(.27,.34,.38),'steel_lt':(.52,.62,.66),
      'steel_dk':(.085,.115,.15),'chrome':(.6,.7,.73),'rock_dark':(.06,.077,.092),
      'roof_grey':(.39,.43,.44),'roof_red':(.49,.09,.028),'red':(.68,.065,.035),
      'glass':(.045,.55,.75),'coal':(.035,.042,.05),'wood':(.42,.21,.065),
      'pine':(.028,.21,.085),'pine_light':(.07,.34,.11),'pine_tip':(.18,.40,.085),
      'bark':(.19,.09,.037),'green':(.04,.36,.15),'sand':(.56,.46,.25),
      'concrete':(.46,.51,.5),'ground':(.29,.37,.10),'foam':(.19,.76,.80),
      'headlight':(.93,.79,.31),'window_warm':(.93,.57,.11),
      'rust':(.18,.13,.07),'yellow_lt':(1,.68,.015),'teal':(.012,.37,.42),
      'green_ind':(.07,.36,.13),'taillight':(.7,.025,.01),'wood_lt':(.52,.29,.08),
      'plant':(.13,.16,.185),'marker_green':(.02,.40,.025),
      'flow':(1,.36,.005),'flow_core':(1,.92,.30),'focus':(.59,1,.005),
    }
    for i,c in enumerate([(.26,.30,.31),(.35,.37,.36),(.42,.43,.40),(.49,.48,.43),(.31,.35,.38),(.59,.56,.48)]): colors['rock'+str(i)]=c
    for name,c in colors.items():
        mat(name,c,rough=.65,metal=.22 if name in ['chrome','steel','steel_lt'] else 0,
            emis=c if name in ['flow','flow_core','focus','headlight','glass'] else None,
            emis_str={'flow':5,'flow_core':7,'focus':4,'headlight':1.5,'glass':.18}.get(name,0))
    # Mottled dry olive turf with soil showing through, all procedural and packed.
    m=bpy.data.materials['ground']; n=m.node_tree.nodes; l=m.node_tree.links
    tex=n.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value=.75; tex.inputs['Detail'].default_value=3
    ramp=n.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].position=.24; ramp.color_ramp.elements[0].color=(.16,.255,.055,1)
    ramp.color_ramp.elements[1].position=.73; ramp.color_ramp.elements[1].color=(.56,.48,.265,1)
    e=ramp.color_ramp.elements.new(.49); e.color=(.36,.405,.12,1)
    coord=n.new('ShaderNodeNewGeometry'); l.new(coord.outputs['Position'],tex.inputs['Vector']); tex.inputs['Scale'].default_value=.34
    l.new(tex.outputs['Fac'],ramp.inputs[0]); l.new(ramp.outputs['Color'],n.get('Principled BSDF').inputs['Base Color'])
    bump=n.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.17; bump.inputs['Distance'].default_value=.13
    l.new(tex.outputs['Fac'],bump.inputs['Height']); l.new(bump.outputs['Normal'],n.get('Principled BSDF').inputs['Normal'])

# s = vertical screen progression; t = left/right. The five physical terraces.
BANDS=[('01_Mountain_Mine',0,22,19,43),('02_Coal_Works',27,17,20,46),
       ('03_Blue_Factory',56,12,23,47),('04_Refinery',86,7,20,45),('05_Warehouse',110,2.7,19,40)]

def terrain(C):
    outlines=[]
    for idx,(name,s,z,hs,ht) in enumerate(BANDS):
        count=80; radii=[R.uniform(.965,1.035) for i in range(count)]
        verts=[]
        # Uneven buttressed cliff faces, widening at sea level.
        for k,(fac,height) in enumerate([(1.075,-1.0),(1.045,z*.25),(.995,z*.70),(1,z)]):
            for i in range(count):
                a=2*pi*i/count
                ss=s+hs*cos(a)*radii[i]*fac
                tt=ht*sin(a)*radii[i]*fac
                verts.append(tuple(W(ss,tt,height+(R.uniform(-.7,.7) if k in [1,2] else 0))))
        faces=[]
        for k in range(3):
            for i in range(count):
                j=(i+1)%count; a=k*count+i; b=k*count+j; c=(k+1)*count+j; d=(k+1)*count+i
                faces.extend([(a,b,c),(a,c,d)])
        ob=mesh(name+'_Faceted_Cliffs',verts,faces,['rock'+str(i) for i in range(6)],C)
        for p in ob.data.polygons: p.material_index=R.choices(range(6),[2,3,3,2,2,1])[0]
        top=verts[-count:]
        mesh(name+'_Turf',top,[tuple(range(count))],['ground'],C)
        outlines.append([(s+hs*cos(2*pi*i/count)*radii[i]*1.09,ht*sin(2*pi*i/count)*radii[i]*1.09) for i in range(count)])
        # Edge boulders break up regular cliff rings.
        b=B()
        for i in range(45):
            a=R.uniform(0,2*pi); ss=s+hs*cos(a)*R.uniform(.94,1.06); tt=ht*sin(a)*R.uniform(.94,1.06)
            p=W(ss,tt,R.uniform(max(0,z-7),z-.3)); rr=R.uniform(.65,2.2)
            b.use('rock'+str(R.randrange(5))).sphere(rr,tuple(p),1,scale=(1,R.uniform(.7,1.4),R.uniform(1,2.2)))
        b.make(name+'_Rock_Outcrops',collection=C)
    return outlines

def trees(C):
    # Branch tiers have irregular alternating tips, not smooth toy cones.
    templates=[]
    for variation in range(5):
        b=B().use('bark').conez(.24,.12,2.1)
        h=R.uniform(4.2,6.2)
        for j,(r,z) in enumerate([(1.35,.9),(1.10,2.0),(.79,3.1),(.46,4.0)]):
            for k in range(2):
                b.use(['pine','pine_light','pine_tip'][min(2,(variation+j)%3)]).conez(r*(1-k*.18),.04,h*.39,(.08*sin(j),0,z*h/5+k*.18),seg=7)
        ob=b.make('Pine_Prototype_'+str(variation),collection=C); ob.hide_render=True; ob.hide_viewport=True; templates.append(ob)
    for idx,(_,s,z,hs,ht) in enumerate(BANDS):
        for j in range(34):
            side=-1 if j%2 else 1
            ss=s+R.uniform(-hs*.64,hs*.67)
            tt=side*ht*math.sqrt(max(.1,1-((ss-s)/hs)**2))*R.uniform(.78,.95)
            # clear right-side railway on upper tiers
            if idx<2 and side==1 and abs(ss-(s+6))<8: continue
            src=R.choice(templates); ob=bpy.data.objects.new('Pine_T%02d_%02d'%(idx,j),src.data); C.objects.link(ob)
            ob.location=W(ss,tt,z+.04); sc=R.uniform(.7,1.4); ob.scale=(sc,sc,sc); ob.rotation_euler.z=R.random()*6.28
        b=B()
        for j in range(80):
            ss=s+R.uniform(-hs*.8,hs*.8); tt=R.choice([-1,1])*R.uniform(ht*.60,ht*.85)
            if ((ss-s)/hs)**2+(tt/ht)**2> .98: continue
            p=W(ss,tt,z+.15)
            if j%3: b.use('pine_tip').sphere(R.uniform(.25,.7),tuple(p),1,scale=(1,1,.7))
            else: b.use('rock2').sphere(R.uniform(.2,.65),tuple(p),1)
        b.make('Rim_Scatter_'+str(idx),collection=C)

def mountain(C):
    for i,(s,t,h,r) in enumerate([(-11,-27,17,8),(-14,-17,29,9),(-15,-5,22,8),(-16,7,32,9),(-11,19,25,8),(-7,27,14,7),(-6,-30,12,6),(-4,11,15,6)]):
        vs=[]; fs=[]; N=9
        for level,rad,zz in [(0,1,0),(1,.70,h*.43),(2,.26,h*.90)]:
            for j in range(N):
                a=j*2*pi/N; rr=rad*r*R.uniform(.78,1.16)
                vs.append(W(s,t,22)+Vector((rr*cos(a)+level*.15,rr*sin(a)*.8,zz+R.uniform(-h*.07,h*.07))))
        vs.append(W(s,t,22+h)+Vector((r*.14,-r*.12,0)))
        for level in range(2):
            for j in range(N):
                a=level*N+j; bb=level*N+(j+1)%N; cc=(level+1)*N+(j+1)%N; dd=(level+1)*N+j
                fs.extend([(a,bb,cc),(a,cc,dd)])
        for j in range(N): fs.append((2*N+j,2*N+(j+1)%N,3*N))
        ob=mesh('Mountain_Crag_%02d'%i,vs,fs,['rock'+str(m) for m in range(6)],C)
        for p in ob.data.polygons: p.material_index=R.choices(range(6),[1,2,3,4,1,2])[0]
    b=B()
    # Deep black recess, heavy lintel, framed sides and hazard chevrons.
    box(b,(8,1.4,8),(0,.4,4),'coal')
    for x in [-4.3,4.3]: box(b,(1.1,2,8.7),(x,0,4.35),'steel_dk'); box(b,(1.4,2.4,.8),(x,0,.4),'steel_lt')
    box(b,(10.2,2,1.4),(0,0,8.8),'steel'); box(b,(8.4,.14,.45),(0,-1.05,8.75),'yellow')
    for x in range(-4,5): box(b,(.32,.16,.48),(x,-1.14,8.75),'steel_dk')
    finish(b,'Mine_Portal',C,-3,-13,22)
    b=B().use('rock2').sphere(1,(0,4.7,5),2,scale=(6.5,5.5,8))
    rock=b.make('Mine_Portal_Rock_Enclosure',collection=C,loc=W(-3,-13,22))
    for v in rock.data.vertices: v.co.y=max(1.2,v.co.y)
    b=B()
    for zz,w,d in [(0,7,6),(7,7,6),(13,9,7)]:
        box(b,(w,d,5),(0,0,zz+2.5),'yellow'); box(b,(w+.3,d+.3,.5),(0,0,zz),'steel_dk')
        for x in [-2,0,2]: box(b,(1.3,.12,1.3),(x,-d/2-.03,zz+3),'glass')
    box(b,(4,5,8),(5,-2,4),'steel'); box(b,(5,5.4,2),(5,-2,9),'yellow')
    b.use('coal').conez(2,2.5,1,(5,-2,10))
    for z in [1,4,7,10]: box(b,(.6,1.6,.32),(-4,-2,z),'chrome')
    for z in [1,8,14]:
        box(b,(4.7,.2,3.3),(0,-3.1,z+1.4),'steel_dk')
        for x in [-2.8,2.8]:
            for zz in [.4,2.7]: cylinder(b,.14,.18,(x,-3.2,z+zz),'chrome',(pi/2,0,0),8)
    for x in [-2.3,2.3]: box(b,(.75,5,10),(x,0,5),'steel_dk')
    box(b,(5.9,2.0,.32),(1,-4,7.1),'steel')
    for x in [-1.5,0,1.5,3]: pipe(b,[(x,-5,7.1),(x,-5,8.3)],.08,'yellow')
    pipe(b,[(-1.5,-5,8.3),(3,-5,8.3)],.08,'yellow')
    finish(b,'Yellow_Mine_Tipple',C,-4,1,22)

def roof(b,x,y,z,w,d,m='white'):
    box(b,(w,d,.35),(x,y,z),m)
    for xx in [-1,1]: box(b,(.3,d,.75),(x+xx*(w/2-.1),y,z+.25),'steel_lt')
    for yy in [-1,1]: box(b,(w,.3,.75),(x,y+yy*(d/2-.1),z+.25),'steel_lt')
    box(b,(2.8,2.4,.9),(x-w*.18,y,z+.6),'steel_lt')
    cylinder(b,.85,.15,(x-w*.18,y,z+1.15),'steel_dk')
    for i in range(4): box(b,(.12,1.5,.05),(x-w*.18-.45+i*.3,y,z+1.25),'chrome')
    for dx in [w*.18,w*.35]: box(b,(.9,1.2,.16),(x+dx,y+d*.2,z+.3),'steel_dk')
def windows(b,x,y,z,w,h,side=False):
    box(b,(w,.2,h) if not side else (.2,w,h),(x,y,z),'steel_dk')
    box(b,(w-.25,.23,h-.25) if not side else (.23,w-.25,h-.25),(x,y,z),'glass')
    box(b,(.09,.27,h) if not side else (.27,.09,h),(x,y,z),'white')
def stack(b,x,y,z,r,h,body='steel_dk',band='yellow'):
    cylinder(b,r*1.22,.6,(x,y,z+.3),'steel')
    cylinder(b,r,h,(x,y,z+h/2),body)
    cylinder(b,r*1.015,h*.18,(x,y,z+h*.76),band)
    cylinder(b,r*1.045,.24,(x,y,z+h),'chrome')
    cylinder(b,r*.86,.26,(x,y,z+h+.035),'coal')

def coal_works(C):
    b=B()
    for x,y,w,d,h in [(-3,1,10,9,8),(4,-2,7,8,5),(-4,5,6,5,11)]:
        box(b,(w,d,h),(x,y,h/2),'plant'); roof(b,x,y,h,w+.3,d+.3,'roof_grey')
        for xx in [-.3,.3]: windows(b,x+xx*w,y-d/2-.12,h*.63,1.4,1.5)
    for x in [4.5,9]: stack(b,x,4,0,1.25,17)
    # Inclined conveyor and hopper to the left.
    pipe(b,[(-5,-1,6),(-12,-1,4),(-18,-1,2.8)],.9,'coal')
    for yy in [-2.1,.1]: pipe(b,[(-5,yy,6.1),(-12,yy,4.1),(-18,yy,2.9)],.17,'steel_lt')
    for i in range(16):
        xx=-18+i*.78; zz=2.8+(xx+18)*.24
        box(b,(.25,2.4,.18),(xx,-1,zz),'chrome')
        b.use('coal').sphere(.48,(xx,-1,zz+.45),1)
    for xx in [-8,-13,-17]: box(b,(.35,.35,3),(xx,-1,1.5),'steel_dk')
    for zz in [1,2,3,4,5,6,7]:
        box(b,(2.2,.22,.16),(3,-6.2,zz),'steel_lt')
    for x in [1.6,4.4]: pipe(b,[(x,-6.1,0),(x,-6.1,8)],.12,'steel_dk')
    for x in [-6,-3,0]:
        box(b,(1.5,.25,1.5),(x,-3.7,2.1),'steel_dk')
        for z in [1.6,1.9,2.2,2.5]: box(b,(1.35,.3,.08),(x,-3.85,z),'chrome')
    ob=finish(b,'Coal_Works_Twin_Stacks_Conveyor',C,25,-1,17); ob.scale=(1.15,1.15,1.1)

def factory(C):
    b=B()
    box(b,(26,22,.65),(0,0,.325),'concrete')
    # Hero composition: asymmetric layered block, projecting front office.
    box(b,(15,13,16),(-3,2,8),'blue')
    box(b,(10,10,12),(6,3,6),'blue')
    box(b,(17,8,6),(1,-7,3),'blue_dark')
    box(b,(17.6,8.5,.6),(1,-7,6.25),'blue_light')
    box(b,(5,6,10),(-10,-5,5),'blue')
    for x in [-9,-5,-1,3]:
        for z in [5.5,9.5,13.5]: box(b,(3.65,.18,3.75),(x,-4.57,z),'blue_light' if (x+z)%3<1.5 else 'blue')
    for x in [-5,-1,3,7]: windows(b,x,-11.15,3.4,3.1,3.7)
    for y in [-9,-5,0,4]: windows(b,11.08,y,3.8,2.5,4.3,True)
    # Dark mullions make blue sheet-metal panels legible at phone size.
    for x in [-10.55,-6.7,-2.85,1]: box(b,(.10,.1,15.5),(x,-4.65,8),'blue_dark')
    for zz in [2,6,10,14]: box(b,(15,.12,.09),(-3,-4.7,zz),'blue_dark')
    roof(b,-3,2,16.15,15.6,13.5,'blue_light'); roof(b,6,3,12.15,10.4,10.4,'blue_light')
    box(b,(5.6,4.4,2.0),(-4,2,17.4),'white')
    windows(b,-4,-.24,17.5,3.4,1)
    stack(b,7,4,12.6,1.45,13.0,'blue','blue_light')
    pipe(b,[(10,1,8),(14,1,8),(15.5,0,7),(15.5,-2,3),(15.5,-2,1)],.85)
    for zz in [1.5,3.5,6.2]: cylinder(b,1,.18,(15.5,-2,zz),'steel_dk')
    pipe(b,[(-1,5,16.5),(-1,5,19),(3,5,19),(4,5,17),(4,5,12.5)],.5)
    # Raised silver gear on a square blue sign panel, facing the camera.
    box(b,(.45,6,6),(11.3,2,9),'blue_dark')
    cylinder(b,2.0,.28,(11.65,2,9),'white',(0,pi/2,0),24)
    for i in range(12):
        a=i*2*pi/12
        b.use('white').box((.34,.82,.82),(11.65,2+2.05*cos(a),9+2.05*sin(a)),(a,0,0))
    cylinder(b,1.0,.31,(11.85,2,9),'blue_dark',(0,pi/2,0),20)
    cylinder(b,.63,.33,(12.04,2,9),'blue_light',(0,pi/2,0),20)
    # Door canopy, steps, foundation piers and bollards.
    box(b,(3.6,.3,4.6),(-9,-8.1,2.3),'steel_dk'); box(b,(4,2,.45),(-9,-8.4,5),'blue_light')
    for i in range(4): box(b,(4.3,1.1,.26*(4-i)),(-9,-8.8-i*.7,.13*(4-i)),'white')
    for x in [-11,-5,1,7,11]: box(b,(1.2,1.2,1.1),(x,-11.2,.55),'steel')
    ob=finish(b,'Hero_Blue_Factory',C,56,0,12,.10); ob.scale=(1.22,1.22,1.10)

def refinery(C):
    b=B()
    box(b,(26,18,.45),(0,0,.225),'concrete')
    box(b,(11,9,10),(-4,0,5),'steel')
    for z in [3.0,8.3]: box(b,(11.5,9.4,1.9),(-4,0,z),'yellow')
    roof(b,-4,0,10.15,11.4,9.4,'steel_lt')
    for x in [-7,-4,-1]: windows(b,x,-4.65,6,2.1,2.7)
    box(b,(4,6,6),(-10,-2,3),'steel_lt')
    for y in [-2,4]: stack(b,8,y,0,2.2,11 if y<0 else 13,'steel','yellow')
    for x in [-7,-3,1]:
        pipe(b,[(x,2,10.5),(x,2,13.0),(x+1.2,2,14),(x+3.2,2,14),(x+4,2,13),(x+4,2,8)],.42)
    cylinder(b,1.5,14,(2,-6,2.6),'chrome',(0,pi/2,0))
    for x in [-4,-.5,3,6]: cylinder(b,1.61,.2,(x,-6,2.6),'steel',(0,pi/2,0))
    pipe(b,[(8,4,8),(8,-1,8),(8,-2,6),(10,-2,6),(11,-3,5),(11,-6,1)],.5)
    for x in [0,2]: pipe(b,[(x,-3,6),(x,-5,6),(x+1,-6,5),(x+1,-6,3)],.3)
    for zz in range(1,10): box(b,(1,.25,.15),(-9.6,-2,zz),'yellow')
    ob=finish(b,'Yellow_Refinery_Pipes_Tanks',C,85,1,7); ob.scale=(1.23,1.23,1.1)

def crate(C,s,t,z,size=1.6):
    b=B().use('yellow').boxz((size,size,size))
    for v in [-.36,.36]:
        box(b,(size*.09,size*1.03,size),(v*size,0,size*.5),'wood')
        box(b,(size*1.03,size*.09,size),(0,v*size,size*.5),'wood')
    box(b,(size*.4,.04,size*.4),(0,-size*.52,size*.6),'white')
    return finish(b,'Shipping_Crate',C,s,t,z,.025)

def warehouse(C):
    b=B()
    box(b,(20,13,8),(0,0,4),'steel')
    roof(b,0,0,8.15,21,14,'white')
    for x in [-7,0,7]:
        box(b,(4.6,.2,4.8),(x,-6.6,2.4),'steel_dk')
        for zz in range(1,5): box(b,(4.1,.22,.07),(x,-6.73,zz),'steel_lt')
        box(b,(5.2,2,.4),(x,-7.2,5.2),'yellow')
        windows(b,x,-6.6,6.7,2.2,1.4)
    for yy in [-3,2,5]: windows(b,10.1,yy,5.5,2,2.6,True)
    ob=finish(b,'Warehouse_Three_Loading_Bays',C,108,0,2.7); ob.scale=(1.16,1.16,1.05)
    for i,(s,t,z) in enumerate([(108,21,2.7),(108,27,2.7),(111,24,5),(113,20,2.7)]):
        ob=parts.container('Warehouse_Container_%02d'%i,C=C,col='blue_light' if i%2 else 'blue'); ob.location=W(s,t,z); ob.scale=(.48,.48,.48)
    for s,t in [(105,15),(114,14),(102,24)]: crate(C,s,t,2.7,1.8)

def catmull(points,steps=12):
    out=[]; pp=[Vector(points[0])]+[Vector(p) for p in points]+[Vector(points[-1])]
    for j in range(1,len(pp)-2):
        p0,p1,p2,p3=pp[j-1:j+3]
        for k in range(steps):
            u=k/steps
            out.append(.5*((2*p1)+(-p0+p2)*u+(2*p0-5*p1+4*p2-p3)*u*u+(-p0+3*p1-3*p2+p3)*u*u*u))
    out.append(Vector(points[-1])); return out

def ribbon(name,pts,width,m,C):
    verts=[]
    for i,p in enumerate(pts):
        d=pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]; side=Vector((-d.y,d.x,0)).normalized()*width/2
        verts.extend([p+side,p-side])
    return mesh(name,verts,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(len(pts)-1)],[m],C)

def railway(C,FX):
    # Actual rails and sleepers, plus separately switchable concept glow.
    controls=[(-1,-13,22.16),(8,-9,22.16),(10,5,22.16),(11,22,22.16),(17,27,21),
      (24,27,17.2),(35,24,17.16),(39,6,17.16),(40,-12,17.16),(44,-24,16),
      (52,-28,12.2),(60,-29,12.16),(66,-26,12.16),(73,-18,11.5),
      (78,-19,7.2),(86,-21,7.16),(92,-15,7.16),(97,-20,6.8),
      (103,-23,2.86),(117,-14,2.86),(124,0,2.3),(132,0,1.5)]
    pts=catmull([W(*p) for p in controls])
    bpy.context.view_layer.update()
    tops=[o for o in bpy.data.objects if o.name.endswith('_Turf')]
    floor=[]
    for p in pts:
        h=-.5
        for top in tops:
            hit,co,normal,face=top.ray_cast(Vector((p.x,p.y,100)),Vector((0,0,-1)))
            if hit: h=max(h,co.z)
        floor.append(h); p.z=max(p.z,h+.20)
    # Continuous inclined viaducts across height changes, with no buried rail.
    for _ in range(2):
        for i in range(1,len(pts)):
            d=Vector((pts[i].x-pts[i-1].x,pts[i].y-pts[i-1].y,0)).length
            pts[i].z=max(pts[i].z,pts[i-1].z-.65*d)
        for i in range(len(pts)-2,-1,-1):
            d=Vector((pts[i].x-pts[i+1].x,pts[i].y-pts[i+1].y,0)).length
            pts[i].z=max(pts[i].z,pts[i+1].z-.65*d)
    support=B()
    for i in range(0,len(pts),7):
        p=pts[i]; h=p.z-max(0,floor[i])
        if h>.75: support.use('steel_dk').box((.65,.65,h),(p.x,p.y,p.z-h/2))
    support.make('Railway_Ramp_Supports',collection=C)
    ribbon('Railway_Gravel_Bed',pts,3.2,'sand',C)
    b=B(); dist=0
    for a,p in zip(pts,pts[1:]):
        dist+=(p-a).length
        if dist<.82: continue
        dist=0; d=(p-a).normalized(); yaw=math.atan2(d.y,d.x)
        b.use('wood').box((.24,2.8,.16),tuple(p+Vector((0,0,.08))),(0,0,yaw))
    b.make('Railway_Wooden_Sleepers',collection=C)
    for offset in [-.98,.98]:
        pp=[]
        for i,p in enumerate(pts):
            d=pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]; side=Vector((-d.y,d.x,0)).normalized()
            pp.append(p+side*offset+Vector((0,0,.24)))
        line('Railway_Steel_Rail',pp,.085,'chrome',C)
    # luminous outside edges leave all sleepers and rails visible.
    for offset in [-1.7,1.7]:
        pp=[]
        for i,p in enumerate(pts):
            d=pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]; side=Vector((-d.y,d.x,0)).normalized()
            pp.append(p+side*offset+Vector((0,0,.25)))
        line('Golden_Production_Edge',pp,.12,'flow',FX)
        line('Golden_Production_Core',pp,.035,'flow_core',FX)
    return pts

def fleets(C,pts):
    # Sample arc length so train car spacing follows curves and slopes.
    accum=[0]
    for a,b in zip(pts,pts[1:]): accum.append(accum[-1]+(b-a).length)
    def at(s):
        i=next((i for i,v in enumerate(accum) if v>=s),len(pts)-1); i=max(1,i)
        u=(s-accum[i-1])/max(.001,accum[i]-accum[i-1]); p=pts[i-1].lerp(pts[i],u); d=(pts[i]-pts[i-1]).normalized(); return p,d
    for start,kind,count in [(20,'coal',4),(91,'coal',4),(163,'blue',4),(222,'tank',2)]:
        for j in range(count):
            p,d=at(start+j*5.5)
            if kind=='coal': ob=parts.wagon('Coal_Wagon',True,C=C)
            elif kind=='blue':
                ob=parts.wagon('Blue_Cargo_Wagon',False,C=C)
            else: ob=parts.truck('Rail_Tanker',body='yellow',load='tank',C=C)
            ob.location=p+Vector((0,0,.2)); ob.rotation_euler=(0,-math.asin(d.z),math.atan2(d.y,d.x)); ob.scale=(.56,.56,.56)
            if kind=='blue':
                cargo=parts.container('Blue_Container_On_Rail',C=C,col='blue'); cargo.location=p+Vector((0,0,1.85)); cargo.rotation_euler=ob.rotation_euler; cargo.scale=(.43,.43,.43)
        if kind=='coal':
            p,d=at(start+count*5.5); ob=parts.locomotive('Yellow_Locomotive',C=C); ob.location=p+Vector((0,0,.22)); ob.rotation_euler.z=math.atan2(d.y,d.x); ob.scale=(.56,.56,.7)
            for slot in ob.material_slots:
                if slot.material.name=='red': slot.material=bpy.data.materials['yellow']
    for s,t,z,color,load in [(67,-13,12,'blue','cargo'),(68,21,12,'white',None),(116,-13,2.7,'yellow','cargo')]:
        ob=parts.truck('Site_Truck',body=color,load=load,C=C,trailer=True); ob.location=W(s,t,z); ob.scale=(.75,.75,.75); ob.rotation_euler.z=-.5
    for s,t,z,color in [(10,-22,22,'yellow'),(31,-19,17,'white')]:
        b=B()
        box(b,(9,3.5,.7),(0,0,1.4),'steel_dk')
        for x in [-3.1,-.8,3.0]:
            for side in [-1,1]: parts.wheel(b,x,side*1.9,1.2,1.2,.7)
        box(b,(2.7,3.7,2.6),(3.2,0,3.1),color)
        box(b,(.12,3.2,1.5),(4.59,0,3.65),'glass')
        for side in [-1,1]: box(b,(2.1,.12,1.35),(3.2,side*1.9,3.75),'glass')
        box(b,(3.5,4.3,.35),(3.0,0,4.55),color)
        box(b,(6,4.3,.35),(-1.45,0,2.45),color)
        for side in [-1,1]: box(b,(6,.35,2),(-1.45,side*2.1,3.35),color)
        for x in [-4.3,1.4]: box(b,(.3,4.2,2),(x,0,3.35),color)
        for i in range(22): b.use('coal').sphere(R.uniform(.4,.8),(R.uniform(-3.8,.9),R.uniform(-1.6,1.6),3.9),1)
        ob=finish(b,'Mining_Dump_Truck',C,s,t,z,.045); ob.rotation_euler.z=-.45
    ob=parts.forklift('Factory_Forklift',C=C); ob.location=W(69,31,12); ob.scale=(.9,.9,.9)

def harbor(C,FX):
    b=B()
    # Main pier runs downward, a crane quay branches off to screen left.
    def slab(s,t,sz,z):
        p=W(s,t,z); b.use('concrete').box(sz,tuple(p),(0,0,pi/4))
    slab(127,0,(8,24,1.4),.8)
    slab(122,-23,(24,13,1.4),.8)
    for s,t in [(117,-35),(126,-35),(127,-11),(136,-3),(136,3),(119,3)]:
        p=W(s,t,.7); b.use('steel').box((1,1,3),tuple(p)); b.use('yellow').cyl(.25,.25,tuple(p+Vector((0,0,1.6))),seg=12)
    b.make('Harbor_Concrete_Quays',collection=C)
    # Lattice gantry, scaled in scene to reference proportions.
    cb=B()
    for x in [-7,7]:
        for y in [-3,3]:
            box(cb,(1,1,15),(x,y,7.5),'yellow'); box(cb,(2,1.8,.6),(x,y,.3),'steel')
        for zz in [0,5,10]:
            pipe(cb,[(x,-3,zz),(x,3,zz+5)],.16,'yellow'); pipe(cb,[(x,3,zz),(x,-3,zz+5)],.16,'yellow')
    for y in [-3,3]:
        for zz in [15,17]: pipe(cb,[(-14,y,zz),(12,y,zz)],.28,'yellow')
        for x in range(-14,12,3):
            pipe(cb,[(x,y,15),(x+3,y,17)],.16,'yellow'); pipe(cb,[(x,y,17),(x+3,y,15)],.16,'yellow')
    box(cb,(3,3,2),(-6,0,15),'steel_dk'); box(cb,(2.6,2.6,1.3),(-6,0,15.5),'glass')
    for y in [-1,1]: pipe(cb,[(-11,y,15),(-11,y,7)],.045,'steel_dk')
    box(cb,(5,2.8,.45),(-11,0,7),'yellow')
    crane=finish(cb,'Yellow_Harbor_Lattice_Gantry',C,124,-27,1.5); crane.rotation_euler.z=-.25
    cargo_ship(C)
    for i in range(4):
        o=parts.container('Dock_Container',C=C,col=['yellow','blue','red','green'][i]); o.location=W(121+i*1.9,-18,1.55); o.scale=(.3,.3,.3)
    # Three independent little stone islands.
    for i,t in enumerate([-30,0,30]):
        b=B(); p=W(157,t,0)
        b.use('rock1').box((20,18,2.2),(0,0,0))
        b.use('concrete').box((21,19,.65),(0,0,1.25))
        for x in [-9.5,9.5]:
            for y in [-8.5,8.5]: b.use('white').box((1.3,1.3,1),(x,y,1.7))
        for x in [-8,-4,0,4,8]:
            for y in [-7,-3,1,5]: b.use('steel_lt').box((3.8,3.8,.035),(x,y,1.595))
        ob=finish(b,'Customer_Island_'+str(i+1),C,157,t,0,.18)
        shop(C,157,t,1.65,i)
        for ss,tt in [(153,t+8),(159,t+8)]:
            bb=B().use('pine_light').sphere(1.2,(0,0,1),1,scale=(1,1,1.7)); finish(bb,'Shop_Bush',C,ss,tt,1.65,0)
    # Visual flow ends at customer islands; not a walkable over-water railway.
    route=[W(132,0,1.5),W(141,0,.55),W(143,0,.55)]
    line('Customer_Flow_Trunk',catmull(route),.16,'flow',FX)
    for t in [-30,0,30]:
        ps=catmull([W(143,0,.55),W(143,t*.7,.55),W(145,t,.55),W(151,t,.55)])
        line('Customer_Flow_Branch',ps,.15,'flow',FX); line('Customer_Flow_Branch_Core',ps,.04,'flow_core',FX)

def shop(C,s,t,z,variant):
    b=B(); color=['red','steel','orange'][variant]
    h=[7,11,8][variant]
    box(b,(10,9,h),(0,0,h/2),color)
    for x in [-3.1,0,3.1]: windows(b,x,-4.6,h*.42,2.6,h*.64)
    for y in [-2.8,.4,3]: windows(b,5.1,y,h*.43,2.5,h*.64,True)
    roof(b,0,0,h+.2,11,10,'roof_red' if variant==0 else 'white')
    for i in range(7):
        box(b,(1.35,2.6,.25),(-4.05+i*1.35,-5.2,h*.71),'white' if i%2 else ['red','blue','green'][variant])
        box(b,(1.35,.2,.6),(-4.05+i*1.35,-6.4,h*.71-.27),'white' if i%2 else ['red','blue','green'][variant])
    if variant==0:
        cylinder(b,1.05,2,(0,0,h+1.5),'white'); cylinder(b,1.2,.3,(0,0,h+2.55),'roof_red'); cylinder(b,.12,1.7,(.25,0,h+3.3),'white')
    elif variant==1:
        for x in [-3.1,0,3.1]: windows(b,x,-4.65,8.8,2.6,2.8)
        cylinder(b,1.6,.3,(0,0,h+.7),'steel_dk')
    else:
        b.use('orange').roof((11,10,3),(0,0,h+.5)); box(b,(4,.25,3),(0,-5.1,h+1),'white')
        cylinder(b,1.1,.25,(0,-5.3,h+1),'yellow',(pi/2,0,0))
    finish(b,'Customer_'+['Cafe','Trading_Office','Equipment_Market'][variant],C,s,t,z,.07)

def cargo_ship(C):
    outline=[(-16,-3.4),(-12,-4.5),(6,-4.5),(12,-3.3),(16,-1.2),(17,0),(16,1.2),(12,3.3),(6,4.5),(-12,4.5),(-16,3.4)]
    vs=[]; fs=[]; n=len(outline)
    for scale,z in [(.72,-2),(.90,-.8),(1,.0),(1.04,1.6),(.98,1.85)]:
        vs.extend([(x*scale,y*scale,z) for x,y in outline])
    for ring in range(4):
        for i in range(n): fs.append((ring*n+i,ring*n+(i+1)%n,(ring+1)*n+(i+1)%n,(ring+1)*n+i))
    fs.append(tuple(range(4*n,5*n)))
    ob=mesh('Container_Ship_Sculpted_Hull',vs,fs,['red','blue_dark','white'],C); ob.location=W(134,-30,0)
    for p in ob.data.polygons: p.material_index=0 if p.index<n else (2 if p.index>=3*n else 1)
    b=B()
    box(b,(5.2,6.8,2.6),(-11.5,0,3.1),'white'); box(b,(4.8,5.8,1.4),(-11.5,0,5.1),'glass')
    box(b,(5.4,6.4,.32),(-11.5,0,5.95),'white'); box(b,(4.2,5.2,.3),(-11.5,0,4.3),'white')
    cylinder(b,.75,2,(-12,0,7),'yellow'); cylinder(b,.80,.4,(-12,0,8.05),'steel_dk')
    for row in range(3):
        for i in range(5):
            for level in range(1+(i+row)%2):
                x=-6.7+i*3.9; y=(row-1)*2.45; z=2.0+level*1.8
                color=['blue','green','red','yellow','teal'][(row+i+level)%5]
                box(b,(3.7,2.25,1.7),(x,y,z+.85),color)
                for k in range(6): box(b,(.065,2.28,1.55),(x-1.5+k*.6,y,z+.85),'steel_dk')
    for side in [-1,1]:
        for x in range(-15,14,2): pipe(b,[(x,side*4.5,1.7),(x,side*4.5,2.65)],.045,'white')
        pipe(b,[(-15,side*4.5,2.65),(10,side*4.5,2.65),(15,side*1.6,2.65)],.06,'white')
    pipe(b,[(14,0,1.8),(14,0,6)],.12,'white')
    finish(b,'Container_Ship_Deck_and_Cargo',C,134,-30,0,.035)

def industrial_details(C):
    b=B()
    # Factory trim bolts, ventilation grille, roller door and utility pipes.
    for x in [-10.1,-6.4,-2.7,1.0]:
        for z in [6,10,14,15.5]: cylinder(b,.09,.1,(x,-4.77,z),'chrome',(pi/2,0,0),8)
    box(b,(3.3,.25,4.6),(-10,-8.25,2.4),'steel_dk')
    for z in [1,1.5,2,2.5,3,3.5,4]: box(b,(2.8,.3,.09),(-10,-8.43,z),'chrome')
    for z in [2,2.5,3,3.5]: box(b,(.3,3,.10),(11.22,3,z),'steel_lt')
    pipe(b,[(-10,4,1),(-10,4,12),(-10,3,13),(-8,3,13)],.32,'chrome')
    box(b,(4,2.2,.3),(6,-9,6.8),'steel')
    for x in [4,5,6,7,8]: box(b,(.18,1.8,.1),(x,-9,7.0),'chrome')
    for x in [-9.5,-8.3]: pipe(b,[(x,8.6,.3),(x,8.6,16.6)],.09,'chrome')
    for z in range(1,17): pipe(b,[(-9.5,8.6,z),(-8.3,8.6,z)],.065,'chrome')
    ob=finish(b,'Factory_Industrial_Detail',C,56,0,12); ob.scale=(1.22,1.22,1.10)
    b=B()
    for x in [-7,-4,-1,2]:
        pipe(b,[(x,-4.8,.5),(x,-4.8,8),(x,-3.5,9)],.19,'chrome')
        cylinder(b,.5,.18,(x,-5.15,3.5),'yellow',(pi/2,0,0),12)
        cylinder(b,.28,.21,(x,-5.2,3.5),'steel_dk',(pi/2,0,0),12)
    for y in [-2,4]:
        for z in [2,5,8]: disk_ring(b,2.25,z,8,y,'chrome',.12)
        for x in [6.7,7.5]: pipe(b,[(x,y-2.25,.4),(x,y-2.25,10)],.08,'steel_lt')
        for z in range(1,10): pipe(b,[(6.7,y-2.25,z),(7.5,y-2.25,z)],.07,'steel_lt')
    ob=finish(b,'Refinery_Valves_Ladders_Pipework',C,85,1,7); ob.scale=(1.23,1.23,1.1)

def markers(C,cam):
    # Optional reference-style world badges, isolated from the actual map assets.
    def badge(name,s,t,z,w,h,kind):
        b=B(); box(b,(w+.6,h+.6,.4),(0,0,0),'steel_dk'); box(b,(w,h,.5),(0,0,.15),'white')
        if kind=='upgrade':
            box(b,(w-.7,h-.7,.4),(0,0,.45),'marker_green')
            box(b,(1.5,3.9,.18),(0,-.5,.8),'white')
        elif kind=='coin':
            cylinder(b,1.6,.25,(0,0,.6),'yellow'); cylinder(b,1.20,.29,(0,0,.65),'headlight')
        elif kind=='diamond':
            b.use('glass').box((2.2,2.2,.3),(0,0,.6),(0,0,pi/4))
        else:
            for i in range(5):
                a=i*2*pi/5; b.use('yellow').box((.65,2.3,.24),(.7*sin(a),.7*cos(a),.6),(0,0,-a))
        ob=finish(b,name,C,s,t,z,.45); ob.rotation_euler=cam.rotation_euler
        if kind=='upgrade':
            vs=[(-2.6,.0,.82),(0,2.8,.82),(2.6,0,.82)]
            arrow=mesh('Upgrade_Arrow_Tip',vs,[(0,1,2)],['white'],C); arrow.location=ob.location; arrow.rotation_euler=cam.rotation_euler
            dot=B().use('green').cyl(1.25,.5,(w/2-.8,h/2-.8,.8),seg=24)
            box(dot,(1.5,.35,.12),(w/2-.8,h/2-.8,1.1),'white'); box(dot,(.35,1.5,.12),(w/2-.8,h/2-.8,1.1),'white')
            plus=finish(dot,'Upgrade_Plus',C,s,t,z,.07); plus.rotation_euler=cam.rotation_euler
        tail=mesh(name+'_Tail',[(-.8,-h/2,.12),(.8,-h/2,.12),(0,-h/2-1.6,.12)],[(0,2,1)],['white'],C)
        tail.location=ob.location; tail.rotation_euler=cam.rotation_euler
    badge('Factory_Upgrade_Badge',53,33,30,10.5,11,'upgrade')
    for t,kind in [(-30,'coin'),(0,'diamond'),(30,'star')]: badge('Customer_'+kind,153,t,17,7.4,5.3,kind)

def ocean(C,outlines):
    m=mat('ocean',(.008,.20,.31),rough=.27,metal=.15)
    n=m.node_tree.nodes; l=m.node_tree.links; bs=n.get('Principled BSDF')
    tex=n.new('ShaderNodeTexNoise'); tex.inputs['Scale'].default_value=.75; tex.inputs['Detail'].default_value=2
    ramp=n.new('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].color=(.004,.075,.14,1); ramp.color_ramp.elements[1].color=(.025,.39,.44,1)
    coord=n.new('ShaderNodeNewGeometry'); l.new(coord.outputs['Position'],tex.inputs['Vector']); tex.inputs['Scale'].default_value=.55
    l.new(tex.outputs['Fac'],ramp.inputs[0]); l.new(ramp.outputs[0],bs.inputs['Base Color'])
    bump=n.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.3; bump.inputs['Distance'].default_value=.27
    l.new(tex.outputs['Fac'],bump.inputs['Height']); l.new(bump.outputs[0],bs.inputs['Normal'])
    finish(B().use('ocean').box((1400,1400,.15),(0,0,-.25)),'Ocean_Surface',C,0,0,0,0)
    # Broken, quiet foam strokes follow exposed coastline; no full contour rings.
    for idx,outline in enumerate(outlines):
        for i in range(0,len(outline),4):
            s,t=outline[i]
            if abs(t)<BANDS[idx][4]*.7 and idx<4: continue
            points=[W(*outline[(i+j)%len(outline)],.015) for j in range(3)]
            line('Shore_Foam',points,.10,'foam',C)
    for i in range(200):
        s=R.uniform(-20,172); t=R.choice([-1,1])*R.uniform(45,65)
        if i%4==0:
            b=B().use('rock2').sphere(R.uniform(.35,1.15),(0,0,.15),1,scale=(1,1,.8)); finish(b,'Sea_Rock',C,s,t,0,0)
        else:
            pp=[W(s,t,.015),W(s+.2,t+.7,.015),W(s,t+1.7,.015)]
            line('Water_Ripple',pp,R.uniform(.02,.06),'foam',C)

def focus(FX):
    pts=[W(57+14*cos(i*pi/64),25*sin(i*pi/64),12.2) for i in range(129)]
    line('Blue_Factory_Selection_Ring',pts,.23,'focus',FX)
    line('Selection_Ring_Core',[p+Vector((0,0,.02)) for p in pts],.055,'flow_core',FX)

def scene_setup():
    sc=bpy.context.scene; sc.name='Focus Ladder — Reference Reconstruction'
    sc.render.engine='CYCLES'; sc.cycles.samples=32; sc.cycles.use_denoising=True
    sc.render.resolution_x=1080; sc.render.resolution_y=2276; sc.render.resolution_percentage=100
    sc.world=bpy.data.worlds.new('Marine_Atmosphere'); sc.world.color=(.18,.18,.18); sc.world.use_nodes=True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.27,.42,.57,1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value=.40
    C=col('90_Camera_and_Lighting')
    cd=bpy.data.cameras.new('Portrait_Reference_Camera'); cam=bpy.data.objects.new(cd.name,cd); C.objects.link(cam)
    cam.rotation_euler=(math.radians(42),0,pi/4); cam.location=W(60,0,0)+cam.rotation_euler.to_matrix()@Vector((0,0,340))
    cd.type='ORTHO'; cd.ortho_scale=228; cd.lens=50; sc.camera=cam
    for name,loc,power,size,color in [('Warm_Softbox',(-65,-25,125),70000,45,(1,.86,.70)),('Sky_Fill',(60,20,100),30000,70,(.61,.79,1))]:
        ld=bpy.data.lights.new(name,'AREA'); ld.energy=power; ld.shape='DISK'; ld.size=size; ld.color=color
        o=bpy.data.objects.new(name,ld); C.objects.link(o); o.location=W(45,0,0)+Vector(loc); o.rotation_euler=(W(50,0,10)-o.location).to_track_quat('-Z','Y').to_euler()
    ld=bpy.data.lights.new('Sun','SUN'); ld.energy=2.2; ld.angle=.18
    o=bpy.data.objects.new('Sun',ld); C.objects.link(o); o.rotation_euler=(.45,-.45,-.55)
    sc.view_settings.view_transform='AgX'; sc.view_settings.look='AgX - Medium High Contrast'; sc.view_settings.exposure=-.35
    if hasattr(sc,'compositing_node_group'):
        tree=bpy.data.node_groups.new('Portrait_Soft_Glow','CompositorNodeTree'); sc.compositing_node_group=tree
    else:
        sc.use_nodes=True; tree=sc.node_tree
    tree.nodes.clear(); render=tree.nodes.new('CompositorNodeRLayers'); glare=tree.nodes.new('CompositorNodeGlare')
    if glare.inputs.get('Type'):
        glare.inputs['Type'].default_value='Fog Glow'; glare.inputs['Quality'].default_value='High'; glare.inputs['Threshold'].default_value=1.5; glare.inputs['Strength'].default_value=.55
        tree.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor'); comp=tree.nodes.new('NodeGroupOutput')
    else:
        glare.glare_type='FOG_GLOW'; glare.quality='HIGH'; glare.threshold=1.5; comp=tree.nodes.new('CompositorNodeComposite')
    tree.links.new(render.outputs['Image'],glare.inputs['Image']); tree.links.new(glare.outputs['Image'],comp.inputs[0])
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D': area.spaces.active.region_3d.view_perspective='CAMERA'; area.spaces.active.clip_end=2000
    return sc

def build():
    # This script is launched in its own factory-startup process, not the user's open scene.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    palette()
    terrain_col=col('01_Terrain_Editable'); nature=col('02_Trees_and_Rocks'); industry=col('03_Industry_Modules')
    routes=col('04_Railway_Physical'); vehicles=col('05_Vehicles_and_Cargo'); port=col('06_Harbor_and_Customers')
    sea=col('07_Ocean_and_Shore'); fx=col('08_Concept_Glow_Toggle')
    outlines=terrain(terrain_col); trees(nature); mountain(industry); coal_works(industry); factory(industry); refinery(industry); warehouse(industry)
    pts=railway(routes,fx); fleets(vehicles,pts); harbor(port,fx); ocean(sea,outlines); focus(fx)
    for s,t in [(65,-10),(68,3),(60,-17)]: crate(vehicles,s,t,12,1.7)
    industrial_details(industry)
    sc=scene_setup(); markers(col('09_Reference_Markers_Toggle'),sc.camera)
    reference=Path('/var/folders/bx/z71j3z61067107ssjhks3w4r0000gn/T/codex-clipboard-a4b35e39-a86e-4cc8-bc03-f6166d322706.png')
    if reference.exists():
        im=bpy.data.images.load(str(reference),check_existing=True); im.name='REFERENCE — Focus Ladder'; im.use_fake_user=True; im.pack()
    sc['Reference']='codex-clipboard-a4b35e39-a86e-4cc8-bc03-f6166d322706.png'
    sc['Deliverable']='Editable 3D reconstruction; standalone Blender review scene. Unity import deferred.'
    sc['Glow']='Toggle collection 08_Concept_Glow_Toggle to hide the conceptual production highlighting.'
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Focus_Ladder_Island.blend'))
    sc.render.filepath=str(OUT/'01-portrait-preview.png'); bpy.ops.render.render(write_still=True)
    print('BUILD_COMPLETE',str(OUT),len(bpy.data.objects))

if __name__=='__main__': build()
