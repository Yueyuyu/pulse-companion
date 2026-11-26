import assert from 'node:assert/strict';
import {readFile,readdir,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {resolve} from 'node:path';
import {build,createServer} from 'vite';
import react from '@vitejs/plugin-react';
import {createElement} from 'react';
import {renderToStaticMarkup} from 'react-dom/server';

// 只在由 build-installer 创建的固定提交导出目录中运行。没有 publicDir、开发服务器、机器人适配器或源码映射。
const root=resolve('systems/pulse-desktop'), out=resolve('public-renderer');
const publicAccountSlot={name:'pulse-public-account-slot',enforce:'pre',transform(code,id){
  if(!id.replaceAll('\\','/').endsWith('/web/PulseDock.jsx'))return;
  const start=code.indexOf('{(selected.canRead||selected.canAuthorize)&&onApplicationAccountAction&&<div className="pd-account"');
  const end=code.indexOf("{taskState !== 'ready' &&",start);
  assert.ok(start>=0&&end>start,'Pinned account slot changed');
  return "import {PublicAccountControls} from './PublicAccountControls.jsx';\n"+code.slice(0,start)+
    '<PublicAccountControls entry={selected} disabled={disabled} action={onApplicationAccountAction}/>'+code.slice(end);
}};
const config={configFile:false,publicDir:false,base:'./',plugins:[publicAccountSlot,react()],logLevel:'warn'};
await build({...config,root:resolve(root,'desktop'),mode:'production',build:{
  outDir:out,emptyOutDir:true,assetsInlineLimit:0,sourcemap:false,modulePreload:{polyfill:false},
  rollupOptions:{output:{entryFileNames:'assets/desktop.js',assetFileNames:asset=>
    asset.names.some(name=>name.endsWith('.css'))?'assets/desktop.css':'assets/[name][extname]'}},
}});
const assets=['antigravity.svg','claude.svg','cursor.svg','desktop.css','desktop.js','grok.svg','kimi.svg','openai.svg'];
assert.deepEqual((await readdir(out)).sort(),['assets','index.html']);
assert.deepEqual((await readdir(resolve(out,'assets'))).sort(),assets);
const html=await readFile(resolve(out,'index.html'),'utf8');
for (const name of ['desktop.js','desktop.css']) assert.ok(html.includes('./assets/'+name),name);
const js=await readFile(resolve(out,'assets/desktop.js'),'utf8');
assert.doesNotMatch(js,/grok-bot-engine|original-data\.js|pulse-local\/|__pulse-local__|mountPulseBot|sourceMappingURL|只读复用桌面登录/);
const hashes={};
for(const name of ['index.html',...assets.map(asset=>'assets/'+asset)]) {
  hashes[name]=createHash('sha256').update(await readFile(resolve(out,name))).digest('hex');
}
await writeFile(resolve(out,'PUBLIC-BRAND.json'),JSON.stringify({schemaVersion:1,distribution:'public-brand',files:hashes},null,2));

// SSR 使用真正发布的组件及固定快照，不打开窗口。测试选择器和拒绝/允许行为，不用源码字符串冒充界面验证。
const server=await createServer({...config,root,server:{middlewareMode:true,hmr:false},appType:'custom'});
try {
  const {PulseDesktopDock,requestPublicAccountAction}=await server.ssrLoadModule('/web/index.js');
  for (const [id,name,brandIcon] of [['codex','Codex','/openai.svg'],['antigravity','Antigravity','/antigravity.svg'],['zcode','ZCode',null]]) {
    const markup=renderToStaticMarkup(createElement(PulseDesktopDock,{mode:'expanded',onApplicationIconChange(){},
      applications:[{id,name,brandIcon,brandLabel:'Z',iconMode:'robot',quotaState:'unavailable',taskState:'unsupported',tasks:[]}]}));
    assert.ok(markup.includes('data-source="pulse-brand"')&&markup.includes('data-application="'+id+'"'));
    assert.ok(brandIcon?markup.includes(brandIcon):markup.includes('pd-brand-label'));
    assert.doesNotMatch(markup,/图标样式|机器人|pulse-upstream/);
  }
  const sent=[];
  requestPublicAccountAction('cursor','resume-auto',()=>true,(...action)=>sent.push(action));
  requestPublicAccountAction('antigravity','resume-auto',()=>false,(...action)=>sent.push(action));
  requestPublicAccountAction('claude','authorize',()=>false,(...action)=>sent.push(action));
  assert.equal(sent.length,0);
  requestPublicAccountAction('antigravity','resume-auto',message=>message.includes('官方 agy CLI'),(...action)=>sent.push(action));
  requestPublicAccountAction('claude','authorize',message=>message.includes('官方网页登录'),(...action)=>sent.push(action));
  assert.deepEqual(sent,[['antigravity','resume-auto'],['claude','authorize']]);
  for(const [id,label,absent] of [['claude','使用官方网页登录','允许官方 CLI 查询'],['antigravity','允许官方 CLI 查询','使用官方网页登录']]){
    const markup=renderToStaticMarkup(createElement(PulseDesktopDock,{mode:'expanded',onApplicationAccountAction(){},applications:[{
      id,name:id,iconMode:'brand',tasks:[],canRead:true,canAuthorize:id==='claude',readMode:'off',quotaState:'unavailable',taskState:'unsupported',authState:'signed-out'}]}));
    assert.ok(markup.includes(label)&&!markup.includes(absent)&&!markup.includes('恢复自动读取'));
  }
  console.log('已验证公开 renderer：品牌 SVG / 名称回退，无图标选择器，第三方读取需明确同意。');
} finally {await server.close();}
