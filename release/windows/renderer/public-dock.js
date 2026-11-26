import {createElement} from 'react';
import {PulseDesktopDock as SharedDock} from './PulseDock.jsx';

export function requestPublicAccountAction(id, action, confirm, send) {
  if (action==='resume-auto' && (id!=='antigravity'||!confirm('允许使用已登录的官方 agy CLI 查询 Antigravity 额度吗？\n\n不会读取桌面会话或发送提示词，随时可以停止。'))) return;
  if (action==='authorize') {
    const names={cursor:'Cursor',claude:'Claude','grok-bot':'Grok Bot（Cursor 官方账户）'};
    if(!Object.hasOwn(names,id)||!confirm('打开 '+names[id]+' 官方网页登录并授权 Pulse 查询额度吗？\n\n不会读取原应用登录状态，可以随时取消或停止读取。'))return;
  }
  send?.(id,action);
}

export function PulseDesktopDock(props) {
  return createElement(SharedDock,{
    ...props,useLogo:true,
    applications:props.applications?.map(entry=>({...entry,iconMode:'brand',sourceLabel:entry.readMode==='web'?'官方网页账户 · 只读':entry.sourceLabel})),
    onApplicationIconChange:undefined,
    onApplicationAccountAction:props.onApplicationAccountAction?
      (id,action)=>requestPublicAccountAction(id,action,message=>window.confirm(message),props.onApplicationAccountAction):undefined,
  });
}
