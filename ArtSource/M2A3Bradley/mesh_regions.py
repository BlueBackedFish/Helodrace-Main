"""Connected regions across OBJ UV seams; no geometry modifications."""
import collections

def position_key(co):return tuple(round(float(x),4) for x in co)

def regions(mesh):
    leaders={};positions={};aliases={}
    def find(i):
        leaders.setdefault(i,i)
        while leaders[i]!=i:leaders[i]=leaders[leaders[i]];i=leaders[i]
        return i
    for v in mesh.vertices:aliases[v.index]=positions.setdefault(position_key(v.co),v.index)
    for p in mesh.polygons:
        a=find(aliases[p.vertices[0]])
        for i in p.vertices[1:]:leaders[find(aliases[i])]=a
    groups=collections.defaultdict(list)
    for p in mesh.polygons:groups[find(aliases[p.vertices[0]])].append(p.index)
    return sorted(groups.values(),key=len,reverse=True)
