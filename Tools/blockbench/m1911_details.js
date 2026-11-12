// M1911 v2 전용 세부 표시 (flat_paint.js 다음에 실행)
//  톱니, 탄피 배출구 안쪽, 총구, 탄창 구멍, 탄창 구멍(점검 구멍), 윗면 가운데 선
const S = 3, tex = Texture.all[0];
const C = n => Cube.all.find(c => c.name === n);
function faceRect(c, face, a0, a1, b0, b1) {
  const f = c.from, t = c.to, uv = c.faces[face].uv; let u0, u1, v0, v1;
  const sx = t[0] - f[0], sy = t[1] - f[1], sz = t[2] - f[2];
  if (face === 'south') { u0 = (a0 - f[0]) / sx; u1 = (a1 - f[0]) / sx; v0 = (t[1] - b1) / sy; v1 = (t[1] - b0) / sy; }
  else if (face === 'north') { u0 = (t[0] - a1) / sx; u1 = (t[0] - a0) / sx; v0 = (t[1] - b1) / sy; v1 = (t[1] - b0) / sy; }
  else if (face === 'up') { u0 = (a0 - f[0]) / sx; u1 = (a1 - f[0]) / sx; v0 = (b0 - f[2]) / sz; v1 = (b1 - f[2]) / sz; }
  else if (face === 'down') { u0 = (a0 - f[0]) / sx; u1 = (a1 - f[0]) / sx; v0 = (t[2] - b1) / sz; v1 = (t[2] - b0) / sz; }
  else if (face === 'east') { u0 = (t[2] - a1) / sz; u1 = (t[2] - a0) / sz; v0 = (t[1] - b1) / sy; v1 = (t[1] - b0) / sy; }
  const U = uv[0] * S, V = uv[1] * S, W = (uv[2] - uv[0]) * S, H = (uv[3] - uv[1]) * S;
  return [Math.round(U + u0 * W), Math.round(V + v0 * H), Math.round(U + u1 * W), Math.round(V + v1 * H)];
}
const ops = [];
const R = (n, face, a0, a1, b0, b1, col) => ops.push([faceRect(C(n), face, a0, a1, b0, b1), col]);
const M0 = '#1E1F23', M1 = '#35373C', M2 = '#555960', M3 = '#8A8F97', DEEP = '#1C191E';
for (const [n, fc] of [['slide_wall_rear', 'south'], ['slide_core', 'north']])
  for (let x = 1.0; x < 5.2; x += 0.67) { R(n, fc, x, x + 0.34, 14.6, 17.8, M0); R(n, fc, x + 0.34, x + 0.67, 14.6, 17.8, M2); }
R('slide_core', 'south', 13, 19, 15.4, 17.9, DEEP); R('slide_core', 'south', 13.3, 18.7, 16.8, 17.6, M2); R('slide_core', 'south', 13.3, 18.7, 17.6, 17.9, M3);
R('slide_top_cap', 'up', 0.6, 29.4, -0.34, 0.34, M2);
R('barrel_tube', 'up', 10, 30.6, -0.34, 0.34, M3);
R('barrel_tube', 'east', -0.67, 0.67, 15.65, 16.95, DEEP);
R('grip_frame', 'down', 1.6, 5.0, -0.95, 0.95, DEEP);
for (const fc of ['south', 'north']) for (const y of [3, 5, 7, 9]) R('mag_body', fc, 3.0, 3.67, y, y + 0.67, DEEP);
tex.edit(canvas => { const ctx = canvas.getContext('2d'); for (const [[x0, y0, x1, y1], col] of ops) { ctx.fillStyle = col; ctx.fillRect(Math.min(x0, x1), Math.min(y0, y1), Math.max(1, Math.abs(x1 - x0)), Math.max(1, Math.abs(y1 - y0))); } }, { edit_name: 'm1911 details' });
return ops.length;
