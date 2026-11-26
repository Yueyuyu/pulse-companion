// 公开版只展示真正编入宿主的两种来源；保持固定 Lab 面板的样式和命令契约。
export function PublicAccountControls({entry,disabled,action}) {
  if(!action||(!entry.canRead&&!entry.canAuthorize))return null;
  const cli=entry.id==='antigravity',pending=entry.authState==='authorizing';
  return <div className="pd-account" aria-label={entry.name+'额度授权'}>
    <p className="pd-empty">{cli?'只调用已安装的官方 agy CLI（1.1.11 或更高），使用 CLI 自己的登录。':
      '只使用你在官方页面主动授权给 Pulse 的账户，不读取桌面应用的登录信息。'}</p>
    {entry.id==='grok-bot'&&<p className="pd-empty">Grok Bot 使用 Cursor 官方账户体系。</p>}
    <div className="pd-account-actions">{pending?
      <button className="pd-account-button" type="button" onClick={()=>action(entry.id,'cancel-authorization')}>取消登录</button>:<>
      {cli&&entry.readMode==='off'&&<button className="pd-account-button" type="button" disabled={disabled} onClick={()=>action(entry.id,'resume-auto')}>允许官方 CLI 查询</button>}
      {!cli&&entry.canAuthorize&&entry.authState!=='connected'&&<button className="pd-account-button" type="button" disabled={disabled||entry.authorizationBlocked||entry.quotaState==='rate-limited'} onClick={()=>action(entry.id,'authorize')}>使用官方网页登录</button>}
      {entry.canDisconnect&&<button className="pd-text-button" type="button" onClick={()=>action(entry.id,'disconnect-account')}>停止读取</button>}
    </>}</div>
  </div>;
}
