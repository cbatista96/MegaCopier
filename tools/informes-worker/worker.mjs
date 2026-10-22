// Receptor privado. No registra direcciones IP, cuerpos de petición ni secretos.
const encoder = new TextEncoder();
const response = (status, accepted = false, code) => Response.json(code ? { accepted, code } : { accepted }, { status, headers: { 'Cache-Control': 'no-store' } });
const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(value))), x => x.toString(16).padStart(2, '0')).join('');
const fields = ['schema','id','created','version','windows','language','mode','theme','activity','kind','trace','recentLog','native','hash'];
const safeTrace = /^(?:at (?:System|Microsoft|MegaCopier|WinRT)\.[A-Za-z0-9_.+`<>]+\(\)|(?:System|Microsoft|MegaCopier|WinRT)\.[A-Za-z0-9_.+`]+(?:Exception|Error)(?: 0x[0-9a-fA-F]{8})?)$/;
export async function validate(r) {
    if (!r || Object.keys(r).sort().join() !== [...fields].sort().join() || r.schema !== 1) return false;
    const match = (key, regex, max) => typeof r[key] === 'string' && r[key].length <= max && regex.test(r[key]);
    if (!match('id', /^[a-f0-9]{32}$/, 32) || !match('hash', /^[a-f0-9]{64}$/,64)
        || !match('version', /^\d{1,4}\.\d{1,4}\.\d{1,4}$/, 16) || !match('windows', /^\d{1,6}(?:\.\d{1,6}){1,3}$/, 28)
        || !match('language', /^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,3}$/, 32) || !match('theme', /^[a-z0-9-]{1,64}$/, 64)
        || !['installed','portable','development'].includes(r.mode) || !['idle','copying','paused','settings','dragging'].includes(r.activity)
        || !['unexpected','managed','native'].includes(r.kind) || !match('created', /^[0-9TZ:+.\-]+$/, 40) || !Number.isFinite(Date.parse(r.created))
        || Date.now() - Date.parse(r.created) > 14*86400000 || Date.parse(r.created) - Date.now() > 86400000) return false;
    for (const key of ['trace','recentLog']) if (typeof r[key] !== 'string' || r[key].length > 12000 || r[key].split('\n').length > 50 || (r[key] && r[key].split('\n').some(line => !safeTrace.test(line)))) return false;
    if (r.native !== null && (!r.native || Object.keys(r.native).sort().join() !== 'code,module,offset'
        || typeof r.native.module !== 'string' || !/^(?:<modulo>|MegaCopier\.exe|(?:Microsoft\.[A-Za-z0-9_.-]+|Windows\.[A-Za-z0-9_.-]+|System\.[A-Za-z0-9_.-]+|ntdll|kernel32|KERNELBASE|combase|ucrtbase|coreclr|CoreMessaging|CoreMessagingXP)\.dll)$/.test(r.native.module)
        || typeof r.native.code !== 'string' || typeof r.native.offset !== 'string'
        || !/^(?:0x)?[0-9a-f]{1,16}$/.test(r.native.code) || !/^(?:0x)?[0-9a-f]{1,16}$/.test(r.native.offset))) return false;
    return r.hash === await digest(`${r.version}\n${r.trace}\n${r.native?.module ?? ''}\n${r.native?.code ?? ''}\n${r.native?.offset ?? ''}`);
}
async function read(request) {
    if (request.headers.get('content-type')?.split(';')[0] !== 'application/json' || Number(request.headers.get('content-length')) >= 64000) return null;
    if (!request.body) return null;
    const reader = request.body.getReader(); const parts = []; let size = 0;
    while (true) { const {value,done} = await reader.read(); if (done) break; size += value.length; if(size >= 64000) { await reader.cancel(); return null; } parts.push(value); }
    const bytes = new Uint8Array(size); let offset=0; for (const part of parts) {bytes.set(part, offset); offset+=part.length;}
    try { const report = JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes)); return await validate(report) ? report : null; } catch { return null; }
}
export default {
    async fetch(request, env) {
        try {
            if (request.method !== 'POST' || new URL(request.url).pathname !== '/report') return response(404);
            if (!env.GITHUB_TOKEN || !env.RATE_LIMIT_SALT || !env.REPORT_RATE_LIMITER || !env.INBOX) return response(503);
            const ip = request.headers.get('CF-Connecting-IP'); if (!ip) return response(400);
            const key = await crypto.subtle.importKey('raw', encoder.encode(env.RATE_LIMIT_SALT), {name:'HMAC',hash:'SHA-256'},false,['sign']);
            const identity = Array.from(new Uint8Array(await crypto.subtle.sign('HMAC',key,encoder.encode(ip))),x=>x.toString(16).padStart(2,'0')).join('');
            if (!(await env.REPORT_RATE_LIMITER.limit({key:identity})).success) return response(429);
            const report = await read(request); if (!report) return response(400);
            // El objeto duradero no recibe ni conserva la IP; serializa cuotas y entregas.
            return env.INBOX.getByName('inbox').fetch(new Request('https://internal/report',{method:'POST', headers:{'Content-Type':'application/json'},body:JSON.stringify(report)}));
        } catch { return response(503); }
    }
};
export class ReportInbox {
    constructor(ctx,env) {
        this.ctx=ctx; this.env=env; this.sql=ctx.storage.sql;
        this.sql.exec('CREATE TABLE IF NOT EXISTS receipts (id TEXT PRIMARY KEY, hash TEXT NOT NULL, day TEXT NOT NULL, delivered INTEGER NOT NULL DEFAULT 0)');
        this.sql.exec('CREATE TABLE IF NOT EXISTS groups (hash TEXT PRIMARY KEY, issue INTEGER NOT NULL, commentday TEXT NOT NULL)');
        this.sql.exec('CREATE TABLE IF NOT EXISTS quota (day TEXT PRIMARY KEY, attempts INTEGER NOT NULL DEFAULT 0, issues INTEGER NOT NULL DEFAULT 0)');
    }
    fetch(request) { return this.ctx.blockConcurrencyWhile(async()=> { try { return await this.accept(request); } catch(error) { return response(503,false,/^github-[0-9]{3}$/.test(error?.message ?? '') ? error.message : `receiver-${this.stage ?? 'start'}-${error instanceof TypeError ? 'type' : 'error'}`); } }); }
    async github(path, method='GET', body) {
        this.stage='github-config';
        if (!/^[A-Za-z0-9_.-]+$/.test(this.env.GITHUB_OWNER ?? '') || !/^[A-Za-z0-9_.-]+$/.test(this.env.GITHUB_REPO ?? '')) throw Error('config');
        const url = `https://api.github.com/repos/${this.env.GITHUB_OWNER}/${this.env.GITHUB_REPO}${path}`;
        const remaining=this.deadline-Date.now(); if(remaining<=0) throw Error('timeout');
        this.stage='github-fetch';
        // Workers admite manual, pero no redirect:error. Todo 3xx falla por !ok sin reenviar el token.
        const reply=await (this.env.TEST_FETCH ?? fetch)(url,{method,redirect:'manual',signal:AbortSignal.timeout(Math.min(8000,remaining)), headers:{'Authorization':`Bearer ${this.env.GITHUB_TOKEN}`,'User-Agent':'MegaCopier-Reports','Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2026-03-10','Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
        if (!reply.ok && !(path==='/labels' && reply.status===422)) throw Error('github-'+reply.status);
        this.stage='github-json';
        return reply.status===204 || reply.status===422 ? null : reply.json();
    }
    async accept(request) {
        // Presupuesto total inferior al límite del bloqueo del objeto duradero.
        this.deadline=Date.now()+14000;
        this.stage='read';
        const r=await read(request); if(!r) return response(400);
        this.stage='receipt';
        const old=this.sql.exec('SELECT * FROM receipts WHERE id=?',r.id).toArray()[0];
        if (old && old.hash!==r.hash) return response(400);
        if (old?.delivered) return response(202,true);
        const day=new Date().toISOString().slice(0,10);
        this.stage='quota';
        this.sql.exec('INSERT OR IGNORE INTO quota(day) VALUES(?)',day);
        const quota=this.sql.exec('SELECT * FROM quota WHERE day=?',day).toArray()[0];
        if(quota.attempts>=2000) return response(429);
        this.sql.exec('UPDATE quota SET attempts=attempts+1 WHERE day=?',day);
        this.sql.exec('DELETE FROM receipts WHERE day < ?',new Date(Date.now()-14*86400000).toISOString().slice(0,10));
        this.sql.exec('DELETE FROM quota WHERE day < ?',new Date(Date.now()-30*86400000).toISOString().slice(0,10));
        if(!old) this.sql.exec('INSERT INTO receipts(id,hash,day) VALUES(?,?,?)',r.id,r.hash,day);
        // Se comprueba ANTES de cualquier creación o comentario; falla si el destino es público.
        if((await this.github('')).private!==true) return response(503);
        this.stage='group';
        let group=this.sql.exec('SELECT * FROM groups WHERE hash=?',r.hash).toArray()[0];
        const title=`[MegaCopier ${r.version}] ${r.hash}`;
        const label='mc-'+r.hash.slice(0,32);
        const text=`~~~~json\n${JSON.stringify(r,null,2)}\n~~~~`;
        if(!group) {
            const existing=await this.github(`/issues?state=all&labels=${label}&per_page=100`);
            let issue=existing.find(x=>x.title===title && !x.pull_request);
            if(!issue) {
                if(quota.issues>=50) return response(429);
                this.sql.exec('UPDATE quota SET issues=issues+1 WHERE day=?',day);
                await this.github('/labels','POST',{name:label,color:'b60205'});
                issue=await this.github('/issues','POST',{title,body:text,labels:[label]});
            }
            if(!Number.isSafeInteger(issue.number) || issue.number<1) throw Error('issue');
            group={hash:r.hash,issue:issue.number,commentday:day};
            this.sql.exec('INSERT INTO groups(hash,issue,commentday) VALUES(?,?,?)',r.hash,group.issue,day);
        } else if(group.commentday!==day) {
            const marker=`<!-- megacopier:${r.hash}:${day} -->`;
            let found=false;
            for(let page=1;page<=10;page++) {
                const comments=await this.github(`/issues/${group.issue}/comments?since=${day}T00%3A00%3A00Z&per_page=100&page=${page}`);
                found=comments.some(x=>x.body?.includes(marker));
                if(found || comments.length<100) break;
                if(page===10) throw Error('pagination');
            }
            if(!found) {
                const count=this.sql.exec('SELECT COUNT(*) AS n FROM receipts WHERE hash=?',r.hash).toArray()[0].n;
                await this.github(`/issues/${group.issue}/comments`,'POST',{body:`${marker}\nRepeticiones recibidas (últimos 14 días): ${count}\n\n${text}`});
            }
            this.sql.exec('UPDATE groups SET commentday=? WHERE hash=?',day,r.hash);
        }
        this.sql.exec('UPDATE receipts SET delivered=1 WHERE id=?',r.id);
        return response(202,true);
    }
}
