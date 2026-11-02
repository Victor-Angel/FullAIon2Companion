export function downloadRelease(releases){
 for(const channel of ['stable','beta']){
  const r=releases?.[channel];if(!r||typeof r.version!=='string'||!/^\d+(?:\.\d+){1,3}(?:[-+][\w.-]+)?$/.test(r.version)||r.version.length>40)continue;
  if(!Number.isSafeInteger(r.bytes)||r.bytes<=0||typeof r.sha256!=='string'||!/^[a-f0-9]{64}$/i.test(r.sha256)||typeof r.id!=='string'||!/^[a-f0-9]{32}$/.test(r.id))continue;
  if(r.url!==`/api/operations.php?action=download&id=${r.id}`)continue;
  const i18n={};if(r.notesI18n&&typeof r.notesI18n==='object')for(const [k,v] of Object.entries(r.notesI18n))if(/^[a-z]{2}$/.test(k)&&typeof v==='string'&&v)i18n[k]=v;
  return {...r,channel,notes:typeof r.notes==='string'?r.notes:'',notesI18n:i18n,sha256:r.sha256.toLowerCase()};
 }
 return null;
}
// The notes in the page's language (the server sends them from 1.0.26): English for a language they are not written in, the
// Spanish ones when there are no others.
export function releaseNotes(r,lang){
 const own=r?.notesI18n?.[lang];if(own)return own;
 if(lang!=='es'&&r?.notesI18n?.en)return r.notesI18n.en;
 return r?.notes??'';
}
// Browsers get a plain static copy that the publish script places in /descargas/ for the stable channel: the .exe handed out
// by /api/operations.php?action=download&id=… looked like a malware dropper to Malwarebytes Browser Guard's heuristics
// (2026-10-16). The page links it only after a HEAD request finds it with the published size; the app keeps the API address.
export function staticDownloadUrl(r){
 return r&&r.channel==='stable'&&typeof r.version==='string'&&/^\d+\.\d+\.\d+$/.test(r.version)?`/descargas/FullAIon2Companion-${r.version}-setup.exe`:null;
}
