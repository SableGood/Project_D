// Project_D 무기 칠하기 스타일 (슬라이드 스타일) — Blockbench execute_script 로 실행
// params = { scale: 3, rules: [{match, base, hi, dark}], wood: {...} }
//  - 모든 큐브의 모든 면을 평평한 본색으로 칠함
//  - 옆면: 윗줄 1px = 밝음, 아랫줄 1px = 어둠 (위에서 오는 빛)
//  - 윗면: 본색 + 긴 두 모서리 1px 밝음 / 아랫면: 어둠
//  - 나무: 본색 + 3px 격자 체커링(어둠) + 테두리 어둠 + 윗줄 밝음
//  부품 이름이 재질을 정함 (rules의 match = 정규식, 위에서부터 첫 번째)
const S = params.scale || 3;
const rules = params.rules;
const tex = Texture.all[0];
const ruleFor = n => rules.find(r => new RegExp(r.match).test(n));
const ops = [];
for (const c of Cube.all) {
  const r = ruleFor(c.name); if (!r) continue;
  for (const fc of ['north', 'south', 'east', 'west', 'up', 'down']) {
    const f = c.faces[fc]; if (!f || !f.uv) continue;
    const x = Math.round(Math.min(f.uv[0], f.uv[2]) * S), y = Math.round(Math.min(f.uv[1], f.uv[3]) * S);
    const w = Math.max(1, Math.round(Math.abs(f.uv[2] - f.uv[0]) * S)), h = Math.max(1, Math.round(Math.abs(f.uv[3] - f.uv[1]) * S));
    ops.push({ x, y, w, h, fc, r });
  }
}
tex.edit(canvas => {
  const ctx = canvas.getContext('2d');
  const fill = (col, x, y, w, h) => { ctx.fillStyle = col; ctx.fillRect(x, y, w, h); };
  for (const o of ops) {
    const { x, y, w, h, fc, r } = o;
    if (fc === 'down') { fill(r.dark, x, y, w, h); continue; }
    fill(r.base, x, y, w, h);
    if (r.pattern === 'checker' && fc !== 'up') {
      fill(r.dark, x, y, w, 1); fill(r.dark, x, y + h - 1, w, 1); fill(r.dark, x, y, 1, h); fill(r.dark, x + w - 1, y, 1, h);
      for (let yy = y + 2; yy < y + h - 2; yy += 3)
        for (let xx = x + 2 + ((yy - y) / 3 % 2 ? 1 : 0); xx < x + w - 2; xx += 3) fill(r.dark, xx, yy, 1, 1);
      fill(r.hi, x + 1, y + 1, w - 2, 1);
      continue;
    }
    if (fc === 'up') { if (h >= 3) { fill(r.hi, x, y, w, 1); fill(r.hi, x, y + h - 1, w, 1); } else fill(r.hi, x, y, w, h); continue; }
    if (h >= 3) { fill(r.hi, x, y, w, 1); fill(r.dark, x, y + h - 1, w, 1); }
  }
}, { edit_name: 'flat paint (slide style)' });
return ops.length;
