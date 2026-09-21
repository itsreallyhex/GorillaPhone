namespace GorillaPhone.Video
{
    /// <summary>JavaScript the mod puts into the browser's pages. It is our own code, run in a browser we started.</summary>
    public static class PageScripts
    {
        /// <summary>The name of the binding the page calls to hand sound to the mod (Runtime.addBinding).</summary>
        public const string AudioBinding = "gpAudio";

        /// <summary>
        /// Injected into every new document (Page.addScriptToEvaluateOnNewDocument). It routes each audio and video element through
        /// Web Audio into a muted output, so the PC stays silent, and sends the samples out through the binding as "rate,base64"
        /// (16-bit interleaved stereo). Also defines __gpPause and __gpResume, which the mod calls when the Video page closes and
        /// opens, so a video does not go on playing behind the home screen. Verified on the desktop by the audio spike (2026-09-21).
        /// Media from another origin without CORS comes out as silence (a browser rule); TikTok's blob media is fine.
        /// </summary>
        public const string AudioHook = @"(function(){
if(window.__gpHooked)return;window.__gpHooked=1;
var ctx=null,proc=null,was=[],hold=false;
function b64(u8){var s='',i,n=u8.length,CH=0x2000;for(i=0;i<n;i+=CH)s+=String.fromCharCode.apply(null,u8.subarray(i,Math.min(n,i+CH)));return btoa(s);}
function ensure(){
 if(ctx)return;
 var AC=window.AudioContext||window.webkitAudioContext;if(!AC)return;
 ctx=new AC({latencyHint:'interactive'});
 proc=ctx.createScriptProcessor(2048,2,2);
 var g=ctx.createGain();g.gain.value=0;proc.connect(g);g.connect(ctx.destination);
 proc.onaudioprocess=function(e){
  var l=e.inputBuffer.getChannelData(0),r=e.inputBuffer.numberOfChannels>1?e.inputBuffer.getChannelData(1):l,n=l.length,o=new Int16Array(n*2);
  for(var i=0;i<n;i++){var a=Math.max(-1,Math.min(1,l[i])),b=Math.max(-1,Math.min(1,r[i]));o[2*i]=a*32767;o[2*i+1]=b*32767;}
  try{window.gpAudio(ctx.sampleRate+','+b64(new Uint8Array(o.buffer)));}catch(x){}
 };
 if(ctx.state!=='running')ctx.resume();
}
function hook(v){
 if(!v||v.__gp)return;v.__gp=1;
 try{ensure();if(!ctx)return;var s=ctx.createMediaElementSource(v);s.connect(proc);if(ctx.state!=='running')ctx.resume();}catch(x){v.__gpErr=String(x);}
}
var play=HTMLMediaElement.prototype.play;
HTMLMediaElement.prototype.play=function(){hook(this);return play.apply(this,arguments);};
setInterval(function(){var m=document.querySelectorAll('video,audio');for(var i=0;i<m.length;i++)hook(m[i]);},500);
window.__gpPause=function(){hold=true;was=[];var m=document.querySelectorAll('video,audio');for(var i=0;i<m.length;i++)if(!m[i].paused){was.push(m[i]);try{m[i].pause();}catch(x){}}};
setInterval(function(){if(!hold)return;var m=document.querySelectorAll('video,audio');for(var i=0;i<m.length;i++)if(!m[i].paused){if(!was.length)was.push(m[i]);try{m[i].pause();}catch(x){}}},250);
window.__gpState=function(){var m=document.querySelectorAll('video,audio'),o=[];for(var i=0;i<m.length&&i<6;i++){var v=m[i];o.push((v.paused?'paused':'playing')+' t='+Math.round(v.currentTime)+' rs='+v.readyState+(v.muted?' muted':'')+' vol='+v.volume+(v.__gp?' hooked':' NOT-hooked')+(v.__gpErr?' err='+v.__gpErr:''));}return 'ctx='+(ctx?ctx.state:'none')+' waiting='+was.length+' media=['+o.join('; ')+']';};
window.__gpResume=function(){hold=false;for(var i=0;i<was.length;i++){try{var p=was[i].play();if(p&&p.catch)p.catch(function(){});}catch(x){}}was=[];};
})();";

        /// <summary>
        /// Defines __gpSnap(x, y, r): the page point to click for a tap at (x, y). If the element under (x, y) shows a pointer cursor
        /// (the page's own sign that it is clickable) that is (x, y) itself; otherwise the nearest point within r pixels that does,
        /// probed on growing rings; otherwise (x, y) again. Returns "x,y".
        /// </summary>
        public const string TapAssist = @"(function(){
if(window.__gpSnap)return;
function hit(px,py){try{var e=document.elementFromPoint(px,py);return !!e&&getComputedStyle(e).cursor==='pointer';}catch(x){return false;}}
window.__gpSnap=function(x,y,r){
 if(hit(x,y))return x+','+y;
 for(var ring=4;ring<=r;ring+=4){
  for(var k=0;k<16;k++){var a=k*Math.PI/8,px=Math.round(x+Math.cos(a)*ring),py=Math.round(y+Math.sin(a)*ring);if(hit(px,py))return px+','+py;}
 }
 return x+','+y;
};
})();";

        public const string Pause ="window.__gpPause&&window.__gpPause()";
        public const string Resume = "window.__gpResume&&window.__gpResume()";

        /// <summary>A phone's user agent, used when [Video] Mobile is on so the site serves its phone layout.</summary>
        public const string MobileUserAgent =
            "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";
    }
}
