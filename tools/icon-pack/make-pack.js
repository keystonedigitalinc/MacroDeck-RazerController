// Builds a .macroDeckIconPack (Macro Deck export format) from a folder of PNGs.
// Step 1: node make-pack.js html <srcDir> <workDir>      -> writes enc.html (encode to WebP in headless Chrome)
// Step 2: node make-pack.js pack <srcDir> <workDir> <out> -> reads Chrome's dumped DOM, writes the pack zip
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { execFileSync } = require('child_process');
const [mode, srcDir, work, out] = process.argv.slice(2);
const files = fs.readdirSync(srcDir).filter((f) => f.toLowerCase().endsWith('.png')).sort();

if (mode === 'html') {
  const list = files.map((f) => ({ n: f.slice(0, -4), u: 'file:///' + path.resolve(srcDir, f).replace(/\\/g, '/').split('/').map(encodeURIComponent).join('/').replace(/^file%3A/, 'file:').replace('/%3A', ':') }));
  const list2 = files.map((f) => ({ n: f.slice(0, -4), u: 'file:///' + encodeURI(path.resolve(srcDir, f).replace(/\\/g, '/')) }));
  fs.writeFileSync(path.join(work, 'enc.html'), `<html><body><pre id="o"></pre><script>
const L=${JSON.stringify(list2)};
(async()=>{const r={};for(const i of L){const im=new Image();im.src=i.u;await im.decode();
const c=document.createElement('canvas');c.width=im.naturalWidth;c.height=im.naturalHeight;
c.getContext('2d').drawImage(im,0,0);r[i.n]=c.toDataURL('image/webp',0.92);}
document.getElementById('o').textContent='BEGIN'+JSON.stringify(r)+'END';})().catch(e=>{document.getElementById('o').textContent='ERR '+e;});
</script></body></html>`);
  console.log(files.length + ' files');
  process.exit(0);
}

const webpDir = path.join(work, "webp");
const sha = (b) => crypto.createHash('sha256').update(b).digest('hex');
const now = new Date().toISOString();
const stage = path.join(work, 'stage');
fs.rmSync(stage, { recursive: true, force: true });
const icons = [], packFiles = [];
for (const f of files) {
  const name = f.slice(0, -4);
  const src = fs.readFileSync(path.join(srcDir, f));
  const master = fs.readFileSync(path.join(webpDir, name + '.webp'));
  if (master.slice(8, 12).toString() !== 'WEBP') throw new Error('not webp: ' + name);
  const id = crypto.randomUUID();
  fs.mkdirSync(path.join(stage, 'icons', id), { recursive: true });
  fs.writeFileSync(path.join(stage, 'icons', id, 'master.webp'), master);
  const w = src.readUInt32BE(16), h = src.readUInt32BE(20);
  icons.push({ id, name, width: w, height: h, isAnimated: false, frameCount: null,
    sourceContentHash: 'sha256:' + sha(src), masterContentHash: 'sha256:' + sha(master), declaredSourceContentHash: 'sha256:' + sha(src),
    checksum: sha(src), sourceIconId: null, originalFileName: f, originalFormat: 'PNG', state: 'Ready', processingError: null,
    availableSizes: [], importBatchId: null, createdAt: now, updatedAt: now });
  packFiles.push({ path: `icons/${id}/master.webp`, sha256: 'sha256:' + sha(master), size: master.length });
}
const pack = { id: crypto.randomUUID(), name: 'Razer App Controller Icons', description: 'Unofficial. 48 flat white icons for the Razer App Controller plugin.',
  author: 'Keystone Digital', version: '1.0.0', isDefault: false, isReadOnly: false, sourceType: 'User', sourceId: null, createdAt: now, updatedAt: now,
  ai: { interaction: false, generatedContent: false, generatedAssets: true }, files: packFiles, icons };
fs.writeFileSync(path.join(stage, 'pack.json'), JSON.stringify(pack, null, 2));
fs.rmSync(out, { force: true });
execFileSync('powershell', ['-NoProfile', '-Command', `Compress-Archive -Path '${stage}\\*' -DestinationPath '${out}.zip' -Force; Move-Item '${out}.zip' '${out}' -Force`]);
console.log(`packed ${icons.length} icons -> ${out}`);
