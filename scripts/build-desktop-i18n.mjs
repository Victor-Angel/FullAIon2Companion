import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const i18n = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'desktop', 'FullAIon2Companion', 'i18n');
const en = {}, pt = {}, fr = {};
for (const name of ['panel.tsv', 'overlay.tsv']) {
  for (const line of fs.readFileSync(path.join(i18n, name), 'utf8').split(/\r?\n/)) {
    if (!line.trim()) continue;
    const columns = line.split('\t');
    if (columns.length !== 3 && columns.length !== 4) throw new Error(`${name}: expected 3 or 4 columns: ${line.slice(0, 60)}`);
    const [es, english, portuguese, french] = columns;
    en[es] = english;
    pt[es] = portuguese;
    fr[es] = french || english;
  }
}
fs.writeFileSync(path.join(i18n, 'panel.json'), JSON.stringify({ en, pt, fr }));
console.log(`${Object.keys(en).length} frases`);
