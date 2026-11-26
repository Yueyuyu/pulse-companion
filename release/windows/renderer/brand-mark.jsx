import openai from '../assets/openai.svg';
import './bot-mark.css';

// 复用公开品牌标志和上游尺寸；此入口没有机器人加载器或本机资源路径。
export function BotMark({brandIcon=openai,brandLabel='?'}) {
  return <span className="pd-bot" data-source="pulse-brand" aria-hidden="true">
    {brandIcon?<img className="pd-brand" src={brandIcon} alt=""/>:<span className="pd-brand-label">{brandLabel}</span>}
  </span>;
}
