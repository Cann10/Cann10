// 使い方は build.py の先頭を参照。Playwright が必要（npm i -g playwright）。
let pw; try { pw = require('playwright'); } catch { pw = require(process.env.PLAYWRIGHT_PATH || '/opt/node-tools/node_modules/playwright'); }
const { chromium } = pw;
(async () => {
  const b = await chromium.launch({ args: ['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'] });
  const p = await b.newPage({ viewport: { width: 780, height: 1688 } });
  p.on('console', m => console.log('console:', m.text()));
  await p.goto('file://' + __dirname + '/preview.html');
  const errs = await p.evaluate(() => window.errors);
  if (errs.length) { console.log(errs.join('\n').slice(0, 6000)); }
  const shots = process.argv.slice(2);
  for (const s of shots) {
    const [name, zoom, lx, ly, lz] = s.split(':');
    await p.evaluate(([n, z, l]) => window.draw(n, z, l), [name, zoom ? +zoom : 1, lx ? [+lx, +ly, +lz] : null]);
    await p.screenshot({ path: `${name}${zoom ? '_z' + zoom : ''}.png` });
  }
  await b.close();
})();
