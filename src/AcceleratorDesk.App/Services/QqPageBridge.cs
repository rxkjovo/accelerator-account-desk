using System.Text.Json;

namespace AcceleratorDesk.App.Services;

public static class QqPageBridge
{
    private const string Guard = "if(location.origin!=='https://ssl.zc.qq.com'||location.pathname!=='/v5/index/')return {ok:false,message:'当前不是受支持的 QQ 官方注册页，请返回注册页后再操作。'};";

    private static string Script(string body) => "(()=>{" + Guard + body + "})()";
    private static string Json(string value) => JsonSerializer.Serialize(value);
    private const string SetInput = "function setInput(e,v){if(e.value===v)return;Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));}";
    private const string Accepted = "function accepted(e){return /(?:^|[;&\\s])is_check_agree=1(?:$|[;&\\s])/.test(e.getAttribute('dt-params')||'')||e.getAttribute('aria-checked')==='true'||e.classList.contains('agreement-checkbox--checked')||!!e.querySelector('.agreement-checkbox__box svg');}";

    public static string Fill(string phone, string nickname, string password) => Script(SetInput + $$"""
        const phone=document.querySelector('input[placeholder="输入手机号码"]');
        const nickname=document.querySelector('input[placeholder="输入昵称"]');
        const password=document.querySelector('input[placeholder="输入QQ密码"][type="password"]');
        if(!phone||!nickname||!password)return {ok:false,message:'官方页面结构已变化，请在右侧手动填写；当前资料仍已保留。'};
        setInput(phone,{{Json(phone)}});setInput(nickname,{{Json(nickname)}});setInput(password,{{Json(password)}});
        return {ok:true,message:'注册资料已填写。'};
        """);

    public static string RequestSms() => Script("""
        const button=document.querySelector('.send-code-button');
        if(!button||button.textContent.trim()!=='发送验证码'||button.classList.contains('send-code-button--disabled')||button.getAttribute('aria-disabled')==='true')
            return {ok:false,message:'验证码按钮尚不可用；请查看右侧号码提示或安全验证，勿连续重试。'};
        button.click();return {ok:true,message:'已请求验证码；请留意短信及右侧官方验证提示。'};
        """);

    public static string PrepareSubmission(string code, bool userAcceptedAgreement)
    {
        if (!userAcceptedAgreement) return "({ok:false,message:'请本人阅读协议后点击同意协议并注册。'})";
        return Script(SetInput + Accepted + $$"""
            const code=document.querySelector('input[placeholder="短信验证码"]');
            const agreement=document.querySelector('.agreement-checkbox');
            if(!code||!agreement||!agreement.querySelector('.agreement-checkbox__box'))return {ok:false,message:'未找到官方验证码或协议控件，请在右侧完成注册。'};
            setInput(code,{{Json(code)}});
            if(!accepted(agreement))agreement.querySelector('.agreement-checkbox__box').click();
            return {ok:true,message:'验证码已填写，正在核对官方提交状态。'};
            """);
    }

    public static string Submit(string phone) => Script(Accepted + $$"""
        const phone=document.querySelector('input[placeholder="输入手机号码"]');
        const agreement=document.querySelector('.agreement-checkbox');
        const buttons=[...document.querySelectorAll('button')].filter(e=>e.textContent.trim()==='立即注册');
        if(!phone||phone.value!=={{Json(phone)}}||!agreement||!accepted(agreement)||buttons.length!==1||buttons[0].disabled)
            return {ok:false,message:'官方页面暂不允许提交，请检查验证码、资料及右侧安全验证提示。'};
        buttons[0].click();return {ok:true,message:'已提交，正在等待官方注册结果。'};
        """);

    public static string VisibleText() => Script("return {ok:true,text:document.body?document.body.innerText:''};");
}
