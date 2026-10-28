const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
process.env.TZ='Asia/Tokyo';
const file=require('node:path').resolve(__dirname,'../../src/ApprovalFlow/wwwroot/app.js');
async function harness(){
 const elements=new Map();
 const element=()=>({value:'',textContent:'',hidden:false,handlers:{},children:[],addEventListener(n,f){this.handlers[n]=f;},replaceChildren(...items){this.children=items;},append(...items){this.children.push(...items);}});
 const get=id=>{if(!elements.has(id))elements.set(id,element());return elements.get(id);};
 const h={calls:[],csrfCount:0,authenticated:false,failCsrf:false};
 const ctx=vm.createContext({document:{getElementById:get,createElement:element},Intl,Date,URLSearchParams,console,fetch:async(path,options={})=>{
  h.calls.push({path,options});
  const response=(status,data)=>({ok:status>=200&&status<300,status,json:async()=>data});
  if(path==='/api/csrf')return h.failCsrf?response(503,{}):response(200,{token:'csrf-'+(++h.csrfCount)});
  if(path==='/api/login'){h.authenticated=true;return response(200,{});}
  if(path==='/api/me')return h.authenticated?response(200,{name:'Demo',role:'Requester'}):response(401,{});
  if(path.startsWith('/api/requests'))return h.authenticated?response(200,{total:0,items:[]}):response(401,{});
  throw Error('Unexpected request: '+path);
 }});
 vm.runInContext(fs.readFileSync(file,'utf8'),ctx);
 await new Promise(setImmediate);await new Promise(setImmediate);
 return {...h,state:h,ctx,get};
}
test('401 resets identity and gets anonymous CSRF without reloading',async()=>{
 const h=await harness();vm.runInContext("token='expired-user-token'; user={name:'Old'}; current={id:99}; page=3;",h.ctx);
 h.get('identity').textContent='Old user';
 await assert.rejects(vm.runInContext("api('/requests')",h.ctx));
 assert.equal(vm.runInContext('token',h.ctx),'csrf-2');
 assert.equal(vm.runInContext('user',h.ctx),null);
 assert.equal(vm.runInContext('current',h.ctx),null);
 assert.equal(h.get('login').hidden,false);
 assert.equal(h.get('identity').children.length,0);
});
test('login always refreshes anonymous CSRF before submitting',async()=>{
 const h=await harness();vm.runInContext("token='expired-user-token'",h.ctx);
 h.get('email').value='demo@example.test';h.get('password').value='not-a-real-credential';
 await h.get('loginForm').handlers.submit({preventDefault(){}});
 const login=h.state.calls.find(x=>x.path==='/api/login');
 assert.equal(login.options.headers['X-CSRF-TOKEN'],'csrf-2');
 assert.equal(h.get('workspace').hidden,false);
});
test('failure to refresh CSRF prevents login submission',async()=>{
 const h=await harness();h.state.failCsrf=true;
 await h.get('loginForm').handlers.submit({preventDefault(){}});
 assert.equal(h.state.calls.filter(x=>x.path==='/api/login').length,0);
});
test('UTC timestamps display the actual JST time across midnight',async()=>{
 const h=await harness();
 const shown=vm.runInContext("date('2026-10-28T15:30:00Z')",h.ctx);
 assert.equal(shown,'2026/10/29 0:30:00');
 assert.equal(vm.runInContext("new Date('2026-10-28T15:30:00Z').toLocaleDateString('ja-JP')",h.ctx),'2026/10/29');
});
