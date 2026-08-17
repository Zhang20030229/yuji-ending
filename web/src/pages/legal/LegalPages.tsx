import { Link } from "react-router-dom";

/** 上架材料需要真实可联系的地址，发布前必须替换。 */
const CONTACT_EMAIL = "TODO_替换为真实联系邮箱";

/** 心理健康类应用的免责与求助资源，隐私政策、条款与设置页共用同一份文案。 */
export function SafetyNotice() {
  return (
    <div className="rounded-xl bg-secondary p-4 text-xs leading-6 text-muted-foreground">
      <p className="font-medium text-foreground">遇己不是医疗服务</p>
      <p>
        遇己提供的是自我记录与自我认识的陪伴工具，它给出的任何内容都不构成医疗、心理或法律建议，
        也不能替代专业诊断与治疗。
      </p>
      <p className="mt-2">
        如果你正处在危机中，或有伤害自己的念头，请立刻联系专业支持：全国心理援助热线 12356、
        希望 24 热线 400-161-9995，紧急情况请拨打 120。
      </p>
    </div>
  );
}

function LegalLayout({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <main className="min-h-dvh bg-background">
      <div
        className="mx-auto w-full max-w-[720px] px-5 pb-16 md:px-8"
        style={{ paddingTop: "calc(env(safe-area-inset-top, 0px) + 28px)" }}
      >
        <h1 className="text-2xl font-semibold tracking-[-0.02em] text-foreground">{title}</h1>
        <div className="mt-6 space-y-5 text-sm leading-7 text-foreground">{children}</div>
        <nav className="mt-10 flex gap-4 text-xs text-muted-foreground">
          <Link to="/privacy" className="underline">隐私政策</Link>
          <Link to="/terms" className="underline">用户条款</Link>
          <Link to="/access" className="underline">返回登录</Link>
        </nav>
      </div>
    </main>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="text-base font-medium text-foreground">{title}</h2>
      <div className="mt-2 space-y-2 text-muted-foreground">{children}</div>
    </section>
  );
}

/** 隐私政策：App Store 提交时需要填写这一页的公开地址。 */
export function PrivacyPage() {
  return (
    <LegalLayout title="遇己隐私政策">
      <p>本政策说明遇己收集哪些信息、为什么收集、保存在哪里，以及你如何删除它们。</p>

      <Section title="我们收集的信息">
        <p>账号信息：用户名、密码（仅保存不可逆的哈希）、昵称、性别与出生年月。</p>
        <p>你主动记录的内容：对话文字、「一刻」文字与图片、事件与地点名称、你自己填写的关系与备注。</p>
        <p>由内容派生的信息：情绪标注、事件摘要、对你的「认识」归纳，以及用于检索的向量索引。</p>
        <p>遇己不收集设备广告标识符，不做用户追踪，也不向第三方共享数据用于广告。</p>
      </Section>

      <Section title="信息如何被使用">
        <p>仅用于实现产品功能：呈现你的记录、生成情绪洞察与报告、在对话中回忆起相关的过往。</p>
        <p>为生成这些内容，你的相关文本会发送给模型服务商 MiniMax 完成推理；除此之外不会外传。</p>
        <p>如果部署方启用了对象存储（腾讯云 COS），你上传的图片会保存在该存储服务中。</p>
      </Section>

      <Section title="保存与删除">
        <p>数据保存在部署方自有的服务器数据库中，传输全程使用 HTTPS 加密。</p>
        <p>你可以在「设置 → 删除账号」中随时永久删除账号。删除会移除全部对话、消息、事件、片段、情绪、认识与图片，且不可恢复。</p>
        <p>单条记录也可以随时删除；被你驳回的「认识」不会再作为后续判断的依据。</p>
      </Section>

      <Section title="未成年人">
        <p>遇己面向成年人设计。未满 18 周岁的用户请在监护人知情并同意后使用。</p>
      </Section>

      <SafetyNotice />

      <Section title="联系我们">
        <p>关于隐私的任何问题或数据请求，请联系：{CONTACT_EMAIL}</p>
      </Section>
    </LegalLayout>
  );
}

/** 用户条款：明确遇己的定位边界与免责范围。 */
export function TermsPage() {
  return (
    <LegalLayout title="遇己用户条款">
      <p>使用遇己前，请阅读并同意以下条款。</p>

      <Section title="遇己是什么">
        <p>遇己是一个记录与自我认识的工具。它把你写下的内容整理成事件、情绪与「认识」，帮助你看清自己，但不替你做决定。</p>
        <p>它对你的任何归纳都可以被你驳回；被驳回的内容不会再进入后续判断。</p>
      </Section>

      <SafetyNotice />

      <Section title="你的内容">
        <p>你写入的内容归你所有。我们不会将其用于训练模型，也不会公开或出售。</p>
        <p>请不要上传他人的敏感信息；涉及他人的记录请自行确认已获得对方同意。</p>
      </Section>

      <Section title="账号与使用规范">
        <p>请自行保管账号与密码。禁止利用遇己从事违法活动或上传违法内容。</p>
        <p>你可以随时在应用内删除账号，删除后数据不可恢复。</p>
      </Section>

      <Section title="服务变更与免责">
        <p>模型输出可能出现错误或偏差，遇己不对由此产生的决定与后果承担责任。</p>
        <p>服务可能因维护、升级或第三方依赖变动而暂时不可用。</p>
      </Section>

      <Section title="联系我们">
        <p>如有疑问请联系：{CONTACT_EMAIL}</p>
      </Section>
    </LegalLayout>
  );
}
