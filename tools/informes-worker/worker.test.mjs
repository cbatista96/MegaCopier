import test from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { createHash, randomUUID } from 'node:crypto';
import worker, {ReportInbox,validate} from './worker.mjs';

function report() {
    const r={schema:1,id:randomUUID().replaceAll('-',''),created:new Date().toISOString(),version:'1.7.1',windows:'10.0.26100.0',language:'es-ES',mode:'portable',theme:'fluent-dark',activity:'settings',kind:'managed',trace:'System.InvalidOperationException 0x80131509\nat MegaCopier.App.App.Launch()',recentLog:'',native:null,hash:''};
    r.hash=createHash('sha256').update(`${r.version}\n${r.trace}\n\n\n`).digest('hex'); return r;
}
function fixture() {
    const db=new DatabaseSync(':memory:'); let chain=Promise.resolve(); const state={issues:[],comments:[],posts:0,private:true,fail:false,rate:true};
    const ctx={storage:{sql:{exec(sql,...args){const rows=db.prepare(sql).all(...args);return {toArray:()=>rows};}}},blockConcurrencyWhile(fn){const next=chain.then(fn);chain=next.catch(()=>{});return next;}};
    const env={GITHUB_TOKEN:'solo-prueba',RATE_LIMIT_SALT:'secreto-sintetico-largo',GITHUB_OWNER:'cbatista96',GITHUB_REPO:'informes-prueba',REPORT_RATE_LIMITER:{limit:async()=>({success:state.rate})}};
    env.TEST_FETCH=async(url,options)=>{
        assert.ok(url.startsWith('https://api.github.com/repos/cbatista96/informes-prueba'));assert.equal(options.headers['X-GitHub-Api-Version'],'2026-03-10');
        assert.equal(options.redirect,'manual');
        if(state.fail)return Response.json({}, {status:503});
        const path=new URL(url).pathname;
        if(path.endsWith('/informes-prueba'))return Response.json({private:state.private});
        if(options.method==='POST') {
            state.posts++; const data=JSON.parse(options.body);
            if(path.endsWith('/labels'))return Response.json({});
            if(path.endsWith('/comments')) { state.comments.push(data); return Response.json({id:1}); }
            const issue={number:state.issues.length+1,...data};state.issues.push(issue);return Response.json(issue,{status:201});
        }
        return Response.json(path.endsWith('/comments')?state.comments:state.issues);
    };
    const inbox=new ReportInbox(ctx,env);env.INBOX={getByName:()=>inbox};
    const send=(r,headers={})=>worker.fetch(new Request('https://example.workers.dev/report',{method:'POST',headers:{'Content-Type':'application/json','CF-Connecting-IP':'127.0.0.1',...headers},body:JSON.stringify(r)}),env);
    return{state,env,send,db};
}
test('valida contrato y rechaza datos libres, campos extra y huella incorrecta',async()=>{
    const r=report();assert.equal(await validate(r),true);assert.equal(await validate({...r,user:'nombre'}),false);assert.equal(await validate({...r,hash:'0'.repeat(64)}),false);
    assert.equal(await validate({...r,recentLog:'C:\\Users\\Persona\\privado.pdf'}),false);assert.equal(await validate({...r,trace:'texto libre persona@example.org'}),false);
    const native={module:'CoreMessagingXP.dll',code:'c000027b',offset:'0000000000097976'};
    const n={...r,native};n.hash=createHash('sha256').update(`${n.version}\n${n.trace}\n${native.module}\n${native.code}\n${native.offset}`).digest('hex');
    assert.equal(await validate(n),true);assert.equal(await validate({...n,native:{...native,offset:123}}),false);
});
test('rechaza repositorio público sin crear etiquetas, issues ni comentarios',async()=>{
    const f=fixture();f.state.private=false;assert.equal((await f.send(report())).status,503);assert.equal(f.state.posts,0);f.db.close();
});
test('rechaza redirección de GitHub sin reenviar el secreto a otro origen',async()=>{
    const f=fixture();let calls=0;f.env.TEST_FETCH=async(_url,options)=>{calls++;assert.equal(options.redirect,'manual');return new Response(null,{status:302,headers:{Location:'https://otro.invalid/'}});};
    const reply=await f.send(report());assert.equal(reply.status,503);assert.equal((await reply.json()).code,'github-302');assert.equal(calls,1);assert.equal(f.state.posts,0);f.db.close();
});
test('cuota de IP, límite de cuerpo y ausencia de configuración',async()=>{
    const f=fixture();f.state.rate=false;assert.equal((await f.send(report())).status,429);f.state.rate=true;
    assert.equal((await f.send({...report(),extra:'x'.repeat(64000)})).status,400);f.env.GITHUB_TOKEN='';assert.equal((await f.send(report())).status,503);f.db.close();
});
test('deduplicación concurrente, reintentos e informes repetidos se agrupan',async()=>{
    const f=fixture();const a=report();const responses=await Promise.all([f.send(a),f.send(a),f.send(report())]);assert.ok(responses.every(r=>r.status===202));assert.equal(f.state.issues.length,1);assert.equal(f.state.comments.length,0);
    assert.equal(f.db.prepare('SELECT COUNT(*) AS n FROM receipts').get().n,2);f.db.close();
});
test('fallo temporal no confirma entrega, el mismo ID funciona después',async()=>{
    const f=fixture();const r=report();f.state.fail=true;assert.equal((await f.send(r)).status,503);f.state.fail=false;
    assert.equal((await f.send(r)).status,202);assert.equal(f.state.issues.length,1);assert.equal((await f.send(r)).status,202);assert.equal(f.state.issues.length,1);f.db.close();
});
test('recupera una incidencia creada aunque se pierda la confirmación de GitHub',async()=>{
    const f=fixture();const original=f.env.TEST_FETCH;let lose=true;
    f.env.TEST_FETCH=async(url,options)=>{const reply=await original(url,options);if(lose && options.method==='POST' && new URL(url).pathname.endsWith('/issues')){lose=false;throw Error('respuesta perdida');}return reply;};
    const r=report();assert.equal((await f.send(r)).status,503);assert.equal(f.state.issues.length,1);
    assert.equal((await f.send(r)).status,202);assert.equal(f.state.issues.length,1);assert.equal(f.state.posts,2);f.db.close();
});
test('comentario agrupado una vez al día y presupuesto global persistente',async()=>{
    const f=fixture();await f.send(report());f.db.prepare('UPDATE groups SET commentday=?').run('2000-01-01');assert.equal((await f.send(report())).status,202);
    assert.equal(f.state.comments.length,1);assert.equal((await f.send(report())).status,202);assert.equal(f.state.comments.length,1);
    f.db.prepare('UPDATE quota SET attempts=2000').run();assert.equal((await f.send(report())).status,429);f.db.close();
});
