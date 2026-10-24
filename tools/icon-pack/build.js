// Builds flat white glyph icons as SVG (256 unit grid, rendered at 1024 px).
// Each icon: main = white shapes, cut = shapes punched out of main, badge = optional
// lower-right badge whose symbol is punched out of the badge disc.
const fs = require('fs');
const path = require('path');

const W = 'fill="#fff"';
const K = 'fill="#000"';
const sw = (w, c = '#fff') => `fill="none" stroke="${c}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"`;

// ---------- primitives (each returns {main, cut}) ----------
const mouse = (cx, cy, w = 92, h = 150) => {
  const x = cx - w / 2, y = cy - h / 2;
  return {
    main: `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${w / 2}" ${W}/>`,
    cut: `<line x1="${cx}" y1="${y - 2}" x2="${cx}" y2="${y + h * 0.4}" ${sw(7, '#000')}/>` +
      `<line x1="${x - 2}" y1="${y + h * 0.4}" x2="${x + w + 2}" y2="${y + h * 0.4}" ${sw(7, '#000')}/>` +
      `<rect x="${cx - 7}" y="${y + h * 0.13}" width="14" height="${h * 0.17}" rx="7" ${K}/>`,
  };
};
const person = (cx, cy, s = 1) =>
  `<circle cx="${cx}" cy="${cy - 22 * s}" r="${18 * s}" ${K}/>` +
  `<path d="M${cx - 34 * s} ${cy + 34 * s} a${34 * s} ${30 * s} 0 0 1 ${68 * s} 0 z" ${K}/>`;
const card = (x, y, w = 120, h = 150) => ({
  main: `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" ${W}/>`,
  cut: person(x + w / 2, y + h / 2 - 4),
});
// back cards peek out up-right of the front card; gap stroke separates them
const cardStack = (n, x, y, w = 120, h = 150, d = 22) => {
  let main = '';
  for (let i = n - 1; i >= 0; i--) {
    if (i < n - 1) main += `<rect x="${x + d * i - 7}" y="${y - d * i - 7}" width="${w + 14}" height="${h + 14}" rx="24" ${K}/>`;
    main += `<rect x="${x + d * i}" y="${y - d * i}" width="${w}" height="${h}" rx="18" ${W}/>`;
  }
  return { main, cut: person(x + w / 2, y + h / 2 - 4) };
};
const sun = (cx, cy, r = 34, ray = 30, n = 8, outline = false) => {
  let s = outline ? `<circle cx="${cx}" cy="${cy}" r="${r - 6}" ${sw(12)}/>` : `<circle cx="${cx}" cy="${cy}" r="${r}" ${W}/>`;
  for (let i = 0; i < n; i++) {
    const a = (i * 2 * Math.PI) / n, r1 = r + 16, r2 = r + 16 + ray;
    s += `<line x1="${cx + r1 * Math.cos(a)}" y1="${cy + r1 * Math.sin(a)}" x2="${cx + r2 * Math.cos(a)}" y2="${cy + r2 * Math.sin(a)}" ${sw(14)}/>`;
  }
  return { main: s, cut: '' };
};
const bulb = (cx, cy, s = 1) => ({
  main: `<circle cx="${cx}" cy="${cy - 30 * s}" r="${58 * s}" ${W}/>` +
    `<path d="M${cx - 40 * s} ${cy + 8 * s} L${cx - 26 * s} ${cy + 46 * s} H${cx + 26 * s} L${cx + 40 * s} ${cy + 8 * s} Z" ${W}/>` +
    `<rect x="${cx - 28 * s}" y="${cy + 54 * s}" width="${56 * s}" height="${14 * s}" rx="${7 * s}" ${W}/>` +
    `<rect x="${cx - 20 * s}" y="${cy + 74 * s}" width="${40 * s}" height="${12 * s}" rx="${6 * s}" ${W}/>`,
  cut: `<path d="M${cx - 30 * s} ${cy - 34 * s} a${30 * s} ${30 * s} 0 0 1 ${26 * s} ${-28 * s}" ${sw(10 * s, '#000')}/>`,
});
const controller = (cx, cy, s = 1) => ({
  main: `<path d="M${cx - 60 * s} ${cy - 46 * s} h${120 * s} c${30 * s} 0 ${46 * s} ${20 * s} ${52 * s} ${50 * s} l${10 * s} ${48 * s} c${6 * s} ${28 * s} ${-26 * s} ${44 * s} ${-44 * s} ${22 * s} l${-24 * s} ${-28 * s} h${-108 * s} l${-24 * s} ${28 * s} c${-18 * s} ${22 * s} ${-50 * s} ${6 * s} ${-44 * s} ${-22 * s} l${10 * s} ${-48 * s} c${6 * s} ${-30 * s} ${22 * s} ${-50 * s} ${52 * s} ${-50 * s} z" ${W}/>`,
  cut: `<rect x="${cx - 76 * s}" y="${cy - 10 * s}" width="${40 * s}" height="${12 * s}" rx="${4 * s}" ${K}/>` +
    `<rect x="${cx - 62 * s}" y="${cy - 24 * s}" width="${12 * s}" height="${40 * s}" rx="${4 * s}" ${K}/>` +
    `<circle cx="${cx + 52 * s}" cy="${cy - 14 * s}" r="${8 * s}" ${K}/><circle cx="${cx + 52 * s}" cy="${cy + 14 * s}" r="${8 * s}" ${K}/>` +
    `<circle cx="${cx + 38 * s}" cy="${cy}" r="${8 * s}" ${K}/><circle cx="${cx + 66 * s}" cy="${cy}" r="${8 * s}" ${K}/>`,
});
const keyboard = (cx, cy, w = 190, h = 92) => {
  const x = cx - w / 2, y = cy - h / 2;
  let cut = '';
  const kw = (w - 28) / 8;
  for (let r = 0; r < 2; r++) for (let c = 0; c < 8; c++)
    cut += `<rect x="${x + 14 + c * kw + 2}" y="${y + 14 + r * 22}" width="${kw - 6}" height="15" rx="3" ${K}/>`;
  cut += `<rect x="${x + 14 + kw * 1.5}" y="${y + 62}" width="${kw * 5}" height="15" rx="3" ${K}/>`;
  return { main: `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="16" ${W}/>`, cut };
};
const headset = (cx, cy, s = 1) => ({
  main: `<path d="M${cx - 70 * s} ${cy + 20 * s} v${-20 * s} a${70 * s} ${70 * s} 0 0 1 ${140 * s} 0 v${20 * s}" ${sw(16 * s)}/>` +
    `<rect x="${cx - 88 * s}" y="${cy + 2 * s}" width="${44 * s}" height="${72 * s}" rx="${18 * s}" ${W}/>` +
    `<rect x="${cx + 44 * s}" y="${cy + 2 * s}" width="${44 * s}" height="${72 * s}" rx="${18 * s}" ${W}/>` +
    `<path d="M${cx - 70 * s} ${cy + 70 * s} q${10 * s} ${36 * s} ${50 * s} ${36 * s}" ${sw(10 * s)}/><circle cx="${cx - 18 * s}" cy="${cy + 106 * s}" r="${10 * s}" ${W}/>`,
  cut: '',
});
const rays = (cx, cy, n = 3, len = 26, spread = 70) => {
  let s = '';
  for (let i = 0; i < n; i++) {
    const a = ((-90 - spread / 2 + (spread * i) / (n - 1)) * Math.PI) / 180;
    s += `<line x1="${cx + 8 * Math.cos(a)}" y1="${cy + 8 * Math.sin(a) + len}" x2="${cx + (8 + len) * Math.cos(a)}" y2="${cy + (8 + len) * Math.sin(a) + len}" ${sw(13)}/>`;
  }
  return s;
};
const sparkle = (cx = 216, cy = 40, r = 30) => `<circle cx="${cx}" cy="${cy}" r="${r + 8}" ${K}/><path d="M${cx} ${cy - r} Q${cx + r * 0.16} ${cy - r * 0.16} ${cx + r} ${cy} Q${cx + r * 0.16} ${cy + r * 0.16} ${cx} ${cy + r} Q${cx - r * 0.16} ${cy + r * 0.16} ${cx - r} ${cy} Q${cx - r * 0.16} ${cy - r * 0.16} ${cx} ${cy - r} z" ${W}/>`;
const loopArrows = (cx, cy, r, w = 16, c = '#fff') => {
  const f = c === '#fff' ? W : K;
  const arc = (a0, a1) => {
    const p = (a) => [cx + r * Math.cos((a * Math.PI) / 180), cy + r * Math.sin((a * Math.PI) / 180)];
    const [x0, y0] = p(a0), [x1, y1] = p(a1);
    return `<path d="M${x0} ${y0} A${r} ${r} 0 0 1 ${x1} ${y1}" ${sw(w, c)}/>`;
  };
  const head = (a) => {
    const t = (a * Math.PI) / 180, x = cx + r * Math.cos(t), y = cy + r * Math.sin(t);
    const tx = -Math.sin(t), ty = Math.cos(t), nx = Math.cos(t), ny = Math.sin(t), L = w * 1.25;
    return `<path d="M${x + tx * L * 1.5} ${y + ty * L * 1.5} L${x + nx * L} ${y + ny * L} L${x - nx * L} ${y - ny * L} Z" ${f}/>`;
  };
  return arc(195, 318) + head(318) + arc(15, 138) + head(138);
};
const merge = (...parts) => ({ main: parts.map((p) => (p.main || '') + (p.cut || '')).join(''), cut: '' });

// ---------- badge symbols (drawn black, punched out of the badge disc) ----------
const B = { cx: 194, cy: 194, r: 50 };
const sym = {
  up: `<path d="M194 166 l-24 26 h15 v26 h18 v-26 h15 z" ${K}/>`,
  down: `<path d="M194 222 l-24 -26 h15 v-26 h18 v26 h15 z" ${K}/>`,
  right: `<path d="M222 194 l-26 -24 v15 h-26 v18 h26 v15 z" ${K}/>`,
  left: `<path d="M166 194 l26 -24 v15 h26 v18 h-26 v15 z" ${K}/>`,
  check: `<path d="M172 195 l15 15 l29 -31" ${sw(12, '#000')}/>`,
  x: `<path d="M178 178 l32 32 M210 178 l-32 32" ${sw(12, '#000')}/>`,
  loop: loopArrows(194, 194, 24, 9, '#000'),
  toggle: `<rect x="166" y="180" width="56" height="28" rx="14" ${K}/><circle cx="208" cy="194" r="9" fill="#fff"/>`,
  slider: `<line x1="170" y1="194" x2="218" y2="194" ${sw(8, '#000')}/><circle cx="202" cy="194" r="12" ${K}/>`,
  gauge: `<path d="M168 206 a26 26 0 0 1 52 0" ${sw(9, '#000')}/><line x1="194" y1="206" x2="208" y2="186" ${sw(8, '#000')}/>`,
  bang: `<rect x="188" y="166" width="12" height="34" rx="6" ${K}/><circle cx="194" cy="214" r="7" ${K}/>`,
  question: `<path d="M180 182 a14 14 0 1 1 20 12 c-5 3 -6 6 -6 12" ${sw(10, '#000')}/><circle cx="194" cy="220" r="6" ${K}/>`,
  undo: `<path d="M210 212 a20 20 0 0 0 -8 -34 h-24" ${sw(10, '#000')}/><path d="M184 164 l-16 14 l16 14 z" ${K}/>`,
  num: (n) => `<text x="194" y="214" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-weight="900" font-size="58" ${K}>${n}</text>`,
};

// ---------- icon definitions ----------
const icons = {};
const def = (name, body, badge) => (icons[name] = { body, badge });

const profileCard = card(48, 52, 128, 160);
def('profiles', (() => { const s = cardStack(3, 40, 82, 128, 150); return s; })());
['next', 'previous', 'cycle'].forEach((m) => def(`profile-${m}`, cardStack(2, 40, 66, 128, 160), { next: sym.right, previous: sym.left, cycle: sym.loop }[m]));
[1, 2, 3, 4, 5].forEach((n) => def(`profile-${n}`, profileCard, sym.num(n)));

const m1 = mouse(118, 120, 104, 170);
def('dpi-up', m1, sym.up);
def('dpi-down', m1, sym.down);
def('dpi-set', m1, sym.gauge);
[1, 2, 3, 4, 5].forEach((n) => {
  let bars = '';
  for (let i = 0; i < 5; i++) {
    const y = 196 - i * 32;
    bars += i < n ? `<rect x="168" y="${y}" width="56" height="22" rx="6" ${W}/>` : `<rect x="172" y="${y + 4}" width="48" height="14" rx="4" ${sw(8)}/>`;
  }
  def(`dpi-stage-${n}`, merge(mouse(88, 128, 104, 170), { main: bars }));
});

def('lighting-off', merge(bulb(128, 124, 1.15), { main: '', cut: `<line x1="36" y1="36" x2="220" y2="220" ${sw(34, '#000')}/>` }, { main: `<line x1="40" y1="40" x2="216" y2="216" ${sw(16)}/>` }));
def('lighting-static', { main: `<path d="M128 26 C150 64 196 108 196 154 a68 68 0 0 1 -136 0 C60 108 106 64 128 26 z" ${W}/>`, cut: `<path d="M96 156 a34 34 0 0 0 26 34" ${sw(12, '#000')}/>` });
def('lighting-breathing', { main: `<circle cx="128" cy="128" r="40" ${W}/><path d="M181.6 83.0 A70 70 0 0 1 181.6 173.0" fill="none" stroke="#fff" stroke-width="15" stroke-linecap="round"/><path d="M74.4 173.0 A70 70 0 0 1 74.4 83.0" fill="none" stroke="#fff" stroke-width="15" stroke-linecap="round"/><path d="M206.1 62.4 A102 102 0 0 1 206.1 193.6" fill="none" stroke="#fff" stroke-width="11" stroke-linecap="round"/><path d="M49.9 193.6 A102 102 0 0 1 49.9 62.4" fill="none" stroke="#fff" stroke-width="11" stroke-linecap="round"/>` });
def('lighting-spectrum', (() => {
  let s = '';
  for (let i = 0; i < 6; i++) {
    const a0 = ((i * 60 + 4 - 90) * Math.PI) / 180, a1 = (((i + 1) * 60 - 4 - 90) * Math.PI) / 180, r = 78;
    s += `<path d="M${128 + r * Math.cos(a0)} ${128 + r * Math.sin(a0)} A${r} ${r} 0 0 1 ${128 + r * Math.cos(a1)} ${128 + r * Math.sin(a1)}" fill="none" stroke="#fff" stroke-width="${30 + i * 0}" />`;
  }
  return { main: s + `<circle cx="128" cy="128" r="26" ${W}/>` };
})());
const wave = (y) => `<path d="M30 ${y} c24 -34 48 -34 66 0 s42 34 66 0 s42 -34 66 0" ${sw(15)}/>`;
def('lighting-wave', { main: wave(78) + wave(128) + wave(178) });
def('lighting-reactive', { main: `<rect x="78" y="96" width="100" height="100" rx="20" ${W}/>` + `<line x1="128" y1="70" x2="128" y2="40" ${sw(13)}/><line x1="68" y1="86" x2="46" y2="64" ${sw(13)}/><line x1="188" y1="86" x2="210" y2="64" ${sw(13)}/><line x1="54" y1="146" x2="26" y2="146" ${sw(13)}/><line x1="202" y1="146" x2="230" y2="146" ${sw(13)}/>`, cut: `<rect x="98" y="112" width="60" height="60" rx="10" ${K}/>` });
def('lighting-ripple', { main: `<circle cx="128" cy="128" r="20" ${W}/><circle cx="128" cy="128" r="50" ${sw(12)}/><circle cx="128" cy="128" r="78" ${sw(10)}/><circle cx="128" cy="128" r="104" ${sw(8)}/>` });
const star = (cx, cy, r) => `<path d="M${cx} ${cy - r} Q${cx + r * 0.16} ${cy - r * 0.16} ${cx + r} ${cy} Q${cx + r * 0.16} ${cy + r * 0.16} ${cx} ${cy + r} Q${cx - r * 0.16} ${cy + r * 0.16} ${cx - r} ${cy} Q${cx - r * 0.16} ${cy - r * 0.16} ${cx} ${cy - r} z" ${W}/>`;
def('lighting-starlight', { main: star(100, 120, 72) + star(190, 64, 34) + star(188, 186, 40) + star(46, 214, 20) });
def('lighting-fire', { main: `<path d="M128 22 C140 62 186 86 186 148 C186 196 160 230 128 230 C96 230 70 196 70 150 C70 120 86 100 100 86 C100 112 112 126 124 128 C114 96 116 54 128 22 z" ${W}/>`, cut: `<path d="M128 132 C134 154 156 166 156 192 C156 212 144 224 128 224 C112 224 100 212 100 194 C100 176 112 168 118 156 C120 170 126 176 132 176 C126 160 124 146 128 132 z" ${K}/>` });
def('lighting-audio-meter', { main: [[40, 120], [76, 70], [112, 150], [148, 40], [184, 100], [220, 130]].map(([x, h]) => `<rect x="${x - 14}" y="${228 - h - 50}" width="28" height="${h + 50}" rx="8" ${W}/>`).join('') });
def('lighting-ambient-awareness', merge({ main: `<rect x="52" y="64" width="152" height="104" rx="12" ${W}/><rect x="112" y="168" width="32" height="28" ${W}/><rect x="84" y="196" width="88" height="14" rx="7" ${W}/>`, cut: `<rect x="66" y="78" width="124" height="76" rx="6" ${K}/>` }, { main: [[30, 90, 10, 80], [30, 116, 6, 116], [30, 142, 10, 152], [226, 90, 246, 80], [226, 116, 250, 116], [226, 142, 246, 152], [100, 48, 92, 26], [128, 46, 128, 22], [156, 48, 164, 26]].map(([x1, y1, x2, y2]) => `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" ${sw(10)}/>`).join('') }));
def('lighting-cycle', merge(bulb(128, 122, 0.78), { main: loopArrows(128, 128, 104, 14) }));

def('chroma-all', merge(keyboard(118, 180, 196, 84), mouse(196, 96, 56, 92), headset(84, 62, 0.62), { main: sparkle(226, 30, 22) }));
def('chroma-keyboard', merge(keyboard(128, 150, 216, 100), { main: sparkle() }));
def('chroma-mouse', merge(mouse(112, 136, 110, 176), { main: sparkle(204, 52, 32) }));
def('chroma-mousepad', merge({ main: `<rect x="28" y="64" width="200" height="140" rx="20" ${sw(14)}/>` }, mouse(150, 140, 50, 80), { main: sparkle(222, 60, 28) }));
def('chroma-headset', merge(headset(118, 112, 1.0), { main: sparkle() }));
def('chroma-keypad', merge({ main: `<rect x="44" y="70" width="140" height="150" rx="26" ${W}/><rect x="160" y="140" width="56" height="56" rx="28" ${W}/>`, cut: (() => { let c = ''; for (let r = 0; r < 3; r++) for (let k = 0; k < 4; k++) c += `<rect x="${58 + k * 30}" y="${86 + r * 30}" width="22" height="22" rx="4" ${K}/>`; return c + `<rect x="74" y="180" width="80" height="22" rx="4" ${K}/>`; })() }, { main: sparkle() }));
def('chroma-link', { main: `<rect x="36" y="104" width="112" height="64" rx="32" ${sw(20)}/><rect x="108" y="104" width="112" height="64" rx="32" ${sw(20)}/>` + sparkle() });
def('chroma-synapse', keyboard(118, 118, 196, 96), sym.undo);

const sunB = sun(112, 112, 30, 24, 8);
def('brightness-up', sunB, sym.up);
def('brightness-down', sun(112, 112, 30, 14, 8), sym.down);
def('brightness-set', sunB, sym.slider);
def('brightness-off', merge(sun(128, 128, 34, 26, 8, true), { cut: `<line x1="36" y1="36" x2="220" y2="220" ${sw(34, '#000')}/>` }, { main: `<line x1="40" y1="40" x2="216" y2="216" ${sw(16)}/>` }));

const ctl = controller(112, 108, 0.86);
def('gaming-mode-on', ctl, sym.check);
def('gaming-mode-off', ctl, sym.x);
def('gaming-mode-toggle', ctl, sym.toggle);

def('status-ready', merge(keyboard(96, 124, 150, 78), mouse(196, 116, 56, 92)), sym.check);
def('status-starting', { main: `<rect x="62" y="28" width="132" height="18" rx="9" ${W}/><rect x="62" y="210" width="132" height="18" rx="9" ${W}/><path d="M76 46 h104 c0 46 -40 60 -40 82 s40 36 40 82 h-104 c0 -46 40 -60 40 -82 s-40 -36 -40 -82 z" ${W}/>`, cut: `<path d="M100 196 c4 -22 22 -32 28 -44 c6 12 24 22 28 44 z M104 70 h48 c-4 16 -16 24 -24 30 c-8 -6 -20 -14 -24 -30 z" ${K}/>` });
def('status-no-devices', merge(mouse(112, 120, 104, 170), { cut: `<line x1="30" y1="30" x2="200" y2="210" ${sw(30, '#000')}/>` }, { main: `<line x1="34" y1="34" x2="196" y2="206" ${sw(14)}/>` }), sym.question);
def('status-restart-needed', { main: loopArrows(112, 112, 72, 18) }, sym.bang);
def('status-control-off', { main: `<path d="M84 112 v-30 a44 44 0 0 1 88 0 v30" ${sw(22)}/><rect x="58" y="108" width="140" height="116" rx="20" ${W}/>`, cut: `<circle cx="128" cy="156" r="14" ${K}/><rect x="122" y="160" width="12" height="34" rx="6" ${K}/>` });
def('status-warning', { main: `<path d="M128 30 L232 214 H24 Z" ${W} stroke="#fff" stroke-width="24" stroke-linejoin="round"/>`, cut: `<rect x="117" y="92" width="22" height="70" rx="11" ${K}/><circle cx="128" cy="190" r="13" ${K}/>` });
def('synapse-restart', merge({ main: `<rect x="22" y="40" width="212" height="176" rx="22" ${W}/>`, cut: `<rect x="40" y="78" width="176" height="122" rx="10" ${K}/><circle cx="46" cy="59" r="7" ${K}/><circle cx="68" cy="59" r="7" ${K}/><circle cx="90" cy="59" r="7" ${K}/>` }, { main: loopArrows(128, 139, 36, 13) }));

// ---------- assemble ----------
function svgFor(name, { body, badge }) {
  const b = badge ? `<circle cx="${B.cx}" cy="${B.cy}" r="${B.r + 9}" ${K}/><circle cx="${B.cx}" cy="${B.cy}" r="${B.r}" ${W}/>${badge}` : '';
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="1024" height="1024"><rect width="256" height="256" fill="#000"/>${body.main}${body.cut || ''}${b}</svg>`;
}

const outDir = process.argv[2];
const only = process.argv.slice(3);
fs.mkdirSync(outDir, { recursive: true });
for (const [name, ic] of Object.entries(icons)) {
  if (only.length && !only.includes(name)) continue;
  fs.writeFileSync(path.join(outDir, `${name}.svg`), svgFor(name, ic));
}
console.log(Object.keys(icons).length + ' icons defined');
