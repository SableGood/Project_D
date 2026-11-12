"""mesh JSON (+atlas png) -> Blockbench .bbmodel (free format, meshes + locator groups)"""
import json, uuid, base64, sys
src, png, out, name = sys.argv[1:5]
d = json.load(open(src)); AW, AH = 256, 128
U = lambda: str(uuid.uuid4())
elements, groups, children = [], [], []
def group(nm, origin, rot=(0, 0, 0), kids=None):
    g = {'name': nm, 'uuid': U(), 'export': True, 'locked': False, 'scope': 0, 'selected': False, 'visibility': True,
         '_static': {'properties': {}, 'temp_data': {}}, 'origin': list(origin), 'rotation': list(rot), 'color': 0, 'children': [],
         'reset': False, 'shade': True, 'mirror_uv': False, 'autouv': 0, 'isOpen': False, 'primary_selected': False}
    groups.append(g); return {'uuid': g['uuid'], 'isOpen': False, 'children': kids or []}
pts = {p['name']: p['p'] for p in d['points']}
origins = {'frame': pts['pt_hand_r'], 'mv_mag': pts['pt_mag_top'], 'mv_trigger': [50, 20, 0]}
for g in d['groups']:
    V, UV, T = g['v'], g['uv'], g['t']
    keys = {}; verts = {}
    def key(i):
        p = (V[i*3], V[i*3+1], V[i*3+2])
        if p not in keys: keys[p] = 'v%d' % len(keys); verts[keys[p]] = list(p)
        return keys[p]
    faces = {}
    for k in range(0, len(T), 3):
        ids = T[k:k+3]; ks = [key(i) for i in ids]
        if len(set(ks)) < 3: continue
        faces['f%d' % (k // 3)] = {'uv': {kk: [round(UV[i*2] * AW, 3), round((1 - UV[i*2+1]) * AH, 3)] for kk, i in zip(ks, ids)}, 'vertices': ks, 'texture': 0}
    e = {'name': g['name'] + '_mesh', 'color': 0, 'origin': [0, 0, 0], 'rotation': [0, 0, 0], 'shading': 'flat', 'export': True, 'visibility': True,
         'locked': False, 'render_order': 'default', 'scope': 0, 'allow_mirror_modeling': True, 'type': 'mesh', 'uuid': U(), 'vertices': verts, 'faces': faces}
    elements.append(e); children.append(group(g['name'], origins.get(g['name'], [0, 0, 0]), kids=[e['uuid']]))
for nm, p in pts.items():
    children.append(group(nm, p, (0, 0, 14) if nm == 'pt_mag_axis' else (0, 0, 0)))
root = group('rifle', [60, 20, 0], kids=children); root['isOpen'] = True
tex = {'name': name, 'path': '', 'folder': '', 'namespace': '', 'id': '0', 'group': '', 'scope': 0, 'width': AW, 'height': AH, 'uv_width': AW, 'uv_height': AH,
       'particle': False, 'use_as_default': False, 'layers_enabled': False, 'sync_to_project': '', 'file_format': 'png', 'render_mode': 'default', 'render_sides': 'auto',
       'wrap_mode': 'limited', 'pbr_channel': 'color', 'fps': 7, 'force_translucent': False, 'frame_time': 1, 'frame_order_type': 'loop', 'frame_order': '',
       'frame_interpolate': False, 'visible': True, 'internal': True, 'saved': False, 'uuid': U(),
       'source': 'data:image/png;base64,' + base64.b64encode(open(png, 'rb').read()).decode()}
bb = {'meta': {'format_version': '5.0', 'model_format': 'free', 'box_uv': False}, 'name': name, 'model_identifier': name, 'visible_box': [1, 1, 0],
      'variable_placeholders': '', 'multi_file_ruleset': '', 'variable_placeholder_buttons': [], 'timeline_setups': [], 'unhandled_root_fields': {},
      'resolution': {'width': AW, 'height': AH}, 'elements': elements, 'groups': groups, 'outliner': [root], 'textures': [tex]}
json.dump(bb, open(out, 'w'), separators=(',', ':'))
print(out, len(elements), sum(len(e['faces']) for e in elements), sum(len(e['vertices']) for e in elements))
