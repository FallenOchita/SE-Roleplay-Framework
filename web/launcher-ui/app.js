(() => {
"use strict";
const state={servers:[],favorites:new Set()}, list=document.getElementById("servers"), search=document.getElementById("search"), filter=document.getElementById("filter"), status=document.getElementById("status"), count=document.getElementById("result-count"), key="serpf.community-browser.favorites.v1";
try { const saved=JSON.parse(localStorage.getItem(key)||"[]"); if(Array.isArray(saved)) saved.filter(x=>typeof x==="string").forEach(x=>state.favorites.add(x)); } catch {}
function save(){try{localStorage.setItem(key,JSON.stringify([...state.favorites]));}catch{status.textContent="Favorites are session-only because browser storage is unavailable.";}}
function online(s){return String(s.status||"").toLowerCase()==="online";}
function send(message){const bridge=window.chrome&&window.chrome.webview;if(!bridge){status.textContent="Desktop bridge unavailable. Open this page inside the SE1 RP launcher.";return;}bridge.postMessage({version:1,...message});}
function render(){
 const term=search.value.trim().toLowerCase(), mode=filter.value;
 const rows=state.servers.filter(s=>{const hay=[s.name,s.description,s.region,s.address].filter(Boolean).join(" ").toLowerCase();return (!term||hay.includes(term))&&(mode!=="online"||online(s))&&(mode!=="offline"||!online(s))&&(mode!=="favorites"||state.favorites.has(s.id));}).sort((a,b)=>Number(online(b))-Number(online(a))||String(a.name||"").localeCompare(String(b.name||"")));
 count.textContent=`${rows.length} of ${state.servers.length} communities`;list.replaceChildren();
 if(!rows.length){const p=document.createElement("p");p.className="empty";p.textContent=state.servers.length?"No communities match these filters.":"No servers received yet. Configure the directory service in launcher Settings.";list.append(p);return;}
 for(const s of rows){
  const card=document.createElement("article");card.className="server-card";
  const top=document.createElement("div");top.className="card-top";
  const h=document.createElement("h2");h.textContent=s.name||"Unnamed community";
  const badge=document.createElement("span");badge.className=online(s)?"status online":"status offline";badge.textContent=s.status||"Unknown";top.append(h,badge);
  const desc=document.createElement("p");desc.className="description";desc.textContent=s.description||"No description provided.";
  const details=document.createElement("p");details.className="details";details.textContent=[s.region,Number.isFinite(s.players)?`${s.players}${Number.isFinite(s.maxPlayers)?` / ${s.maxPlayers}`:""} players`:"Player count unavailable"].filter(Boolean).join(" · ");
  const actions=document.createElement("div");actions.className="card-actions";
  const fav=document.createElement("button");fav.type="button";fav.className="favorite";fav.textContent=state.favorites.has(s.id)?"★ Saved":"☆ Save";fav.setAttribute("aria-pressed",String(state.favorites.has(s.id)));fav.addEventListener("click",()=>{state.favorites.has(s.id)?state.favorites.delete(s.id):state.favorites.add(s.id);save();render();});
  const connect=document.createElement("button");connect.type="button";connect.textContent="Connect";connect.disabled=!online(s);connect.addEventListener("click",()=>send({type:"server.connect",serverId:String(s.id)}));actions.append(fav,connect);card.append(top,desc,details,actions);list.append(card);
 }
}
const bridge=window.chrome&&window.chrome.webview;if(bridge)bridge.addEventListener("message",event=>{const m=event.data;if(!m||m.version!==1||m.type!=="servers.updated"||!Array.isArray(m.servers))return;state.servers=m.servers.filter(s=>s&&typeof s.id==="string"&&typeof s.name==="string");status.textContent=`${state.servers.length} communities received from the launcher directory.`;render();});
search.addEventListener("input",render);filter.addEventListener("change",render);document.getElementById("refresh").addEventListener("click",()=>send({type:"servers.refresh"}));render();send({type:"servers.refresh"});
})();