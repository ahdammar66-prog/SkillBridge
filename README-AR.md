# SkillBridge — Full Stack Starter

مشروع تدريبي كامل: React + TypeScript + Vite للواجهة، وASP.NET Core Web API مع SQLite للـ backend وقاعدة البيانات.

## المتطلبات
- Node.js (موجود عندك بالفعل)
- .NET SDK 10 (موجود عندك بالفعل حسب نتيجة `dotnet --version`)
- اتصال إنترنت أول مرة لتنزيل الحزم

## 1) تشغيل الـ Backend
افتحي Terminal أول، ونفّذي من مجلد المشروع:

```powershell
cd server
dotnet restore
dotnet run
```

الـ API هيشتغل على `http://localhost:5080`.
اختبريه بفتح `http://localhost:5080/api/health`، المفروض يظهر `status: ok`.
قاعدة البيانات `skillbridge.db` بتتعمل تلقائيًا داخل مجلد تشغيل السيرفر.

## 2) تشغيل الـ Frontend
افتحي Terminal ثاني في VS Code (سيبي الأول شغال):

```powershell
cd client
npm install
npm run dev
```

افتحي `http://localhost:5173`.

## المزايا الموجودة
- صفحة رئيسية responsive بتصميم ملون ورسومات CSS.
- بحث وتصنيفات للمهارات، والبيانات بتتجاب من الـ API.
- تفاصيل المهارة وطلب جلسة وحفظ الطلب في SQLite.
- إنشاء حساب وتسجيل دخول مع تخزين كلمة المرور كـ hash + salt، وليس كنص صريح.
- endpoint لعرض طلبات الجلسات.

## API endpoints
- `GET /api/health`
- `GET /api/skills?search=react&category=Development`
- `GET /api/skills/{id}`
- `POST /api/auth/register` body: `{ "name": "Ahd Ammar", "email": "ahd@example.com", "password": "secret123" }`
- `POST /api/auth/login` body: `{ "email": "ahd@example.com", "password": "secret123" }`
- `POST /api/bookings` body: `{ "skillId": 1, "learnerName": "Ahd Ammar", "mentorName": "Mariam Hassan" }`
- `GET /api/bookings?learner=Ahd%20Ammar`

## ملاحظة مهمة
ده إصدار تعليمي شغال محليًا. تسجيل الدخول الحالي يتحقق من الحساب وكلمة المرور، لكنه لا ينشئ JWT أو جلسة مصادقة محمية؛ لذلك لازم نضيف JWT/authorization قبل اعتباره جاهزًا للإنتاج، ونضيف validation وrate limiting وسياسات أمان مناسبة.
