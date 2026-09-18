const { chromium } = require('C:/Users/Harry/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
 const host=await browser.newPage({viewport:{width:390,height:844}});
 host.on('pageerror',e=>console.log('HOST ERROR',e.message));
 await host.goto('http://127.0.0.1:5188/');
 await host.waitForFunction(()=>localStorage.getItem('ohhell.playerId'));
 await host.getByRole('button',{name:'Create New Room',exact:true}).click();
 await host.locator('.room-lobby-card').waitFor();
 const invite=await host.locator('.share-input-row input').inputValue();
 console.log('HOST READY',invite);
 const guest=await browser.newPage({viewport:{width:390,height:844}});
 guest.on('pageerror',e=>console.log('GUEST ERROR',e.message));
 await guest.goto(invite);
 await guest.waitForFunction(()=>localStorage.getItem('ohhell.playerId'));
 await guest.getByRole('button',{name:'Join This Room',exact:true}).click();
 try { await guest.locator('.room-lobby-card').waitFor({timeout:8000}); } catch(e) { console.log('GUEST BODY',await guest.locator('body').innerText()); throw e; }
 console.log('JOINED',await host.locator('.lobby-player-row').count());
} finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1});
