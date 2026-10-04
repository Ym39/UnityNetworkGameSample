# EOS (Epic Online Services) 가이드

이 프로젝트에서 EOS를 어떻게 쓰고 있는지 정리한 문서입니다.
개념 → Developer Portal 설정 → 실제 코드 순서로 읽으면 됩니다.

---

## 1. EOS란

Epic이 무료로 제공하는 게임 백엔드입니다. 로그인, 로비, 매치메이킹, P2P 통신,
실적, 랭킹, 클라우드 세이브 등을 서버 운영 없이 쓸 수 있습니다.

- Epic Games Store 전용이 아니고, **어느 스토어/플랫폼에서든** 사용 가능
- 대부분의 기능은 **플레이어가 Epic 계정을 가질 필요가 없습니다**

### 현재 프로젝트 환경

| 항목 | 값 |
|---|---|
| 플러그인 | `com.playeveryware.eos` **v6.1.2** (MIT) |
| 내장 EOS SDK | **1.19.1.2** |
| 설치 방식 | UPM Git URL |
| Unity | 6000.7.0a6 (⚠️ alpha — 플러그인 공식 지원은 6.0 / 6.3 LTS) |

```
https://github.com/EOS-Contrib/eos_plugin_for_unity_upm.git
```

> 플러그인 설치에는 **Git + Git LFS**가 필요합니다. LFS가 없으면 네이티브
> 라이브러리가 포인터 파일로만 받아져 런타임에 실패합니다.

---

## 2. 핵심 개념 5가지

### 2-1. 계층 구조

Developer Portal의 구조가 그대로 설정값이 됩니다.

```
Organization (팀)
└── Product (게임 1개) ············ ProductId
    ├── Clients
    │   └── Client ················ ClientId + ClientSecret
    │       └── Client Policy ····· "무엇을 할 수 있는가" (권한)
    ├── Sandboxes ················· SandboxId   (Dev / Stage / Live)
    └── Deployments ··············· DeploymentId (실제 데이터 저장소)
```

| 값 | 의미 | 주의 |
|---|---|---|
| `ProductId` | 게임 식별자 (32자리 hex) | 변하지 않음 |
| `SandboxId` | 환경 (Dev/Stage/Live) | **환경 간 데이터 완전 분리** |
| `DeploymentId` | 실제 데이터가 쌓이는 곳 | **Sandbox와 쌍으로 맞춰야 함** |
| `ClientId` | 클라이언트 식별자 | **`xyz`로 시작하는 32자** |
| `ClientSecret` | 위와 짝 | 43~44자 base64 |
| `EncryptionKey` | 파일 저장 암호화 키 (64자리 hex) | **바꾸면 기존 데이터 복호화 불가** |

> **흔한 실수**: Portal의 Client "이름"을 `ClientId` 칸에 넣는 것.
> `ClientId`는 반드시 `xyz`로 시작합니다. 틀리면 `InvalidCredentials`가 납니다.

### 2-2. 두 종류의 ID — Auth vs Connect

EOS에는 **서로 다른 두 개의 사용자 ID**가 있습니다. 가장 혼란스러운 부분입니다.

| | **EpicAccountId** | **ProductUserId (PUID)** |
|---|---|---|
| 담당 인터페이스 | **Auth** | **Connect** |
| 정체 | Epic 계정 | 이 게임 안에서의 플레이어 |
| 필요 조건 | Epic 계정 필수 | **불필요** |
| 쓰는 곳 | 친구목록, 프레젠스 | **로비, 세션, P2P, 실적, 랭킹** |

**게임플레이 기능은 전부 `ProductUserId`를 씁니다.** 이 프로젝트는 Epic 계정을
쓰지 않으므로 `EpicAccountId`는 등장하지 않습니다.

### 2-3. Device ID 로그인 (이 프로젝트가 쓰는 방식)

Epic 계정 없이 `ProductUserId`를 발급받는 방법입니다. 모바일 기본 경로입니다.

플러그인 공식 대응표에 따르면 Android/iOS에서는 **Dev Auth를 쓸 수 없습니다.**
(PC 개발 중에 쓰는 `EOS_DevAuthTool`은 모바일에서 동작하지 않음)

```
1. CreateDeviceId          기기에 "디바이스 ID" 등록
        ↓
2. Connect Login           그 ID로 로그인 시도
        ↓ Result.InvalidUser  ← 최초 1회는 반드시 실패함
3. CreateUser              ContinuanceToken으로 EOS 유저 생성
```

**3단계를 빼먹으면 "첫 실행만 로그인 실패"하는 버그가 됩니다.**

알아둬야 할 반환값:

| 결과 | 의미 | 처리 |
|---|---|---|
| `Success` | 성공 | 진행 |
| `DuplicateNotAllowed` | 이미 기기 ID 있음 | **정상으로 간주하고 진행** |
| `InvalidUser` | EOS 유저 미생성 (최초) | **`CreateUser`로** |

> EOS SDK는 `DuplicateNotAllowed`를 Error 레벨 로그로 남깁니다.
> `DeviceId access credentials already exist...` 는 **정상 동작**입니다.

### 2-4. Tick이 없으면 아무것도 안 돌아감

EOS SDK는 콜백을 알아서 호출해주지 않습니다. **매 프레임
`PlatformInterface.Tick()`을 돌려야** 콜백이 배달됩니다.

플러그인의 `EOSManager`가 `Update()`에서 이걸 해줍니다. 그래서 `EOSManager`가
씬에 **GameObject로 존재해야** 합니다. 없으면 에러도 없이 무반응입니다.

### 2-5. 네이티브 핸들은 직접 해제

`LobbyDetails`, `SessionDetails`, `LobbySearch`, `LobbyModification` 등은
네이티브 핸들이라 **GC가 수거하지 않습니다.**

```csharp
search.Release();
details.Release();
modification.Release();
```

방치하면 실기기에서 메모리가 계속 증가합니다.

---

## 3. Lobby / Session / P2P 의 관계

가장 오해하기 쉬운 부분입니다. **셋은 순차 단계가 아니라, 역할이 다른 부품입니다.**

```
Lobby   ─┐
          ├→ ProductUserId 확보 → P2P 직접 통신 → 게임
Session ─┘
```

| | **Lobby** | **Session** | **P2P** |
|---|---|---|---|
| 역할 | 경기 전 대기방 | 경기 광고 레코드 | 데이터 전송 |
| 통신 경로 | Epic 서버 (WebSocket) | 조회형 | **플레이어 직접** |
| 멤버 변동 알림 | ✅ 실시간 | ❌ | ❌ |
| 호스트 이전 | ✅ | ❌ | — |
| 음성채팅 | ✅ 내장 | ❌ | — |
| 시작/종료 상태 | ❌ | ✅ | — |
| 연결 기능 | **없음** | **없음** | 담당 |

### 중요한 사실

**P2P에는 탐색 기능이 전혀 없습니다.** `FindPeers` 같은 API가 없습니다.
상대의 `ProductUserId`를 이미 알고 있어야 통신이 시작됩니다.

- 내가 남을 **찾기**: ❌ 불가능
- 남이 나에게 **연락해오면 알기**: ✅ 가능 (`OnIncomingConnectionRequestInfo.RemoteUserId`)

그래서 "최초 연락처"를 알려줄 중앙 창구가 필요하고, **그게 Lobby의 역할**입니다.
Lobby와 P2P가 만나는 지점은 딱 한 군데입니다.

```csharp
foreach (var member in EOSLobbyManager.GetMembers())   // Lobby → 명단
    EOSP2PManager.Instance.Send(member, payload);      // P2P  → 전송
```

### 결론

**P2P 대전만 할 거면 Lobby만으로 충분합니다.** Session은
"진행 중인 경기를 목록에서 구분하고 싶을 때" 추가로 씁니다 (순차가 아니라 병행).

### 연결은 자동

EOS P2P에는 "연결하기" API가 없습니다. **`SendPacket`을 호출하는 순간이 연결**이고,
상대는 알림을 받아 `AcceptConnection`을 호출하면 성립합니다.

---

## 4. 파일 구성

```
Assets/
├── EOS/
│   └── link.xml                    IL2CPP 코드 제거 방지 (모바일 필수)
├── Editor/
│   └── EOSAndroidGradlePatcher.cs  AGP 9 호환 패치
├── Scripts/
│   ├── EOS/                        ← EOS 래퍼 (여기만 보면 됨)
│   │   ├── EOSBootstrap.cs             EOSManager 자동 생성
│   │   ├── EOSSDKManager.cs            PlatformInterface 접근
│   │   ├── EOSLoginManager.cs          Device ID 로그인
│   │   ├── EOSLobbyManager.cs          로비
│   │   ├── EOSSessionManager.cs        세션
│   │   ├── EOSP2PManager.cs            P2P 송수신
│   │   └── Samples/
│   │       ├── EOSMatchSample.cs       IMGUI 데모
│   │       └── EOSConfigDump.cs        설정 진단용
│   └── Network/                    ← 게임 동기화 계층
│       ├── NetworkMessage.cs           패킷 직렬화
│       ├── NetworkPlayer.cs            캐릭터 1개당 네트워크 신원
│       └── NetworkGameManager.cs       스폰/디스폰 + 패킷 라우팅
└── StreamingAssets/EOS/*.json      설정값 (플랫폼별)
```

### ⚠️ 설정 파일은 플랫폼별로 따로 저장됩니다

```
eos_product_config.json    Product 탭
eos_windows_config.json    ← 에디터가 읽는 파일
eos_android_config.json
eos_ios_config.json
...
```

**`EOS Configuration` 창에서 Product 탭 값을 바꿔도 각 플랫폼 탭에 반영되지 않습니다.**
값을 변경했으면 **사용할 플랫폼 탭을 하나씩 열어 `Save All Changes`**를 눌러야 합니다.

> 실제로 이 프로젝트에서 Android만 수정되고 Windows에 옛 값이 남아
> `InvalidCredentials`로 한참 헤맸습니다.

---

## 5. 코드 — 기본 흐름

### 5-1. 초기화 (자동)

`EOSBootstrap`이 게임 시작 시 `EOSManager`를 만듭니다. **씬 배치 불필요.**

```csharp
// EOSBootstrap.cs
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
private static void Create()
{
    if (Object.FindAnyObjectByType<EOSManager>() != null) return;

    var host = new GameObject(nameof(EOSManager));
    host.AddComponent<EOSManager>();
    host.AddComponent<EOSP2PManager>();
}
```

**`AfterSceneLoad`를 쓴 이유**: Android 전용 모듈(`AndroidPlatformSpecifics`)이
`BeforeSceneLoad`에서 자신을 등록하는데, 같은 로드 타입 안에서는 실행 순서가
보장되지 않습니다. `AddComponent`는 `Awake`를 즉시 실행하므로, 모듈 등록 전에
초기화가 돌면 Android에서 깨집니다.

### 5-2. 로그인 — 모든 기능의 전제

```csharp
EOSLoginManager.LoginWithDeviceId("플레이어이름", result =>
{
    if (result == Result.Success)
    {
        Debug.Log($"내 PUID: {EOSLoginManager.LocalProductUserId}");
        // 여기서부터 Lobby / Session / P2P 사용 가능
    }
});
```

| 멤버 | 설명 |
|---|---|
| `LoginWithDeviceId(name, cb)` | 로그인 (3단계를 내부에서 처리) |
| `LocalProductUserId` | 내 PUID (로그인 전 null) |
| `IsLoggedIn` | 로그인 여부 |

### 5-3. 로비

**호스트**

```csharp
EOSLobbyManager.Create("DEFAULT", 4, result =>
    Debug.Log($"로비 ID: {EOSLobbyManager.CurrentLobbyId}"));
```

**게스트**

```csharp
EOSLobbyManager.Search("DEFAULT", 10, (result, lobbies) =>
{
    if (lobbies == null || lobbies.Count == 0) return;

    var target = lobbies[0];
    lobbies.RemoveAt(0);
    EOSLobbyManager.ReleaseResults(lobbies);   // 안 쓰는 건 반드시 해제

    EOSLobbyManager.Join(target, r =>
    {
        Debug.Log($"참가: {r}");
        target.Release();                      // 참가 후에도 해제
    });
});
```

첫 인자 `"DEFAULT"`가 **매칭 키**입니다. 같은 문자열을 쓰는 사람끼리만 만납니다.

| 멤버 | 설명 |
|---|---|
| `Create(key, max, cb)` | 생성 + 검색용 속성 공개 |
| `Search(key, max, cb)` | 검색 |
| `Join(details, cb)` / `Leave(cb)` / `Destroy(cb)` | 참가 / 퇴장 / 해산 |
| `GetMembers()` | 멤버 PUID 목록 ← **P2P 주소** |
| `GetOwner()` | 방장 PUID |
| `ReleaseResults(list)` | 검색 결과 일괄 해제 |
| `CurrentLobbyId` / `IsInLobby` | 상태 |

#### 생성은 2단계다

```csharp
// EOSLobbyManager.cs
lobby.CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo info) =>
{
    CurrentLobbyId = info.LobbyId;
    AddGameModeAttribute(gameMode, onComplete);   // ← 2단계
});
```

**`CreateLobby`의 `BucketId`는 검색 조건이 아닙니다.** 검색에 걸리게 하려면
생성 후 `UpdateLobbyModification` → `AddAttribute` → `UpdateLobby`로
**속성을 공개**해야 합니다.

```csharp
var attributeOptions = new LobbyModificationAddAttributeOptions
{
    Visibility = LobbyAttributeVisibility.Public,   // ← 이게 없으면 검색 안 됨
    Attribute = new AttributeData { Key = "GAMEMODE", Value = gameMode }
};
```

`Private`으로 하면 로비 내부에서만 보이고 **검색에는 안 나옵니다.**
"만들었는데 상대가 못 찾는" 현상의 대표 원인입니다.

### 5-4. P2P

```csharp
// 받기 — 로그인 후 1회 등록
EOSP2PManager.Instance.PacketReceived += (sender, data) =>
    Debug.Log($"{sender}: {Encoding.UTF8.GetString(data)}");

// 보내기
EOSP2PManager.Instance.Send(remoteUserId, payload);
```

| 멤버 | 설명 |
|---|---|
| `SocketName` (`"GAME"`) | **양쪽이 같아야 연결됨** |
| `PacketReceived` | 수신 이벤트 (메인 스레드) |
| `Send(puid, bytes)` | 전송 (ReliableOrdered) |
| `Disconnect(puid)` | 연결 종료 |
| `IsListening` | 수신 준비 여부 |

#### 수신은 매 프레임 직접 긁어와야 함

```csharp
// EOSP2PManager.cs
while (p2p.GetNextReceivedPacketSize(ref sizeOptions, out uint packetSize) == Result.Success
       && packetSize > 0)
{
    // ReceivePacket ...
    PacketReceived?.Invoke(sender, buffer);
}
```

EOS는 패킷을 내부 큐에 쌓아둘 뿐 콜백을 주지 않습니다. `while`로 도는 이유는
한 프레임에 여러 개가 왔을 때 한 번에 처리하기 위함입니다. `if`로 하면
1프레임에 1패킷만 처리되어 트래픽이 많을 때 지연이 누적됩니다.

#### 접속 요청 자동 수락

```csharp
private void OnConnectionRequest(ref OnIncomingConnectionRequestInfo info)
{
    if (info.SocketId?.SocketName != SocketName) return;   // 무관한 연결 차단
    p2p.AcceptConnection(ref options);                      // ← 없으면 통신 안 됨
}
```

#### ⚠️ 종료 시 네이티브 호출 금지 — 에디터가 죽습니다

```csharp
// EOSP2PManager.cs
EOSManager.Instance.AddApplicationCloseListener(ReleaseHandle);
```

Play 정지 순서는 이렇습니다.

```
Application.quitting
  → EOSManager.OnShutdown()
      → UnloadAllLibraries()     ← EOSSDK DLL을 프로세스에서 언로드
  → 씬 오브젝트 파괴 → OnDestroy()
      → 여기서 네이티브 호출하면 💥 (액세스 위반, 에디터 통째로 종료)
```

`AddApplicationCloseListener`는 `OnShutdown()`의 **맨 처음**, DLL 언로드 전에
실행되므로 여기서 구독 해제를 끝내야 합니다.

> `Application.quitting`을 직접 쓰면 안 됩니다. `EOSManager`가 먼저 구독하고
> 있어서 델리게이트 실행 순서상 EOSManager의 종료가 먼저 돌아버립니다.

### 5-5. 세션 (선택)

```csharp
EOSSessionManager.Create("DEFAULT", 4, cb);
EOSSessionManager.Start(cb);    // 경기 시작 → 검색에서 제외 가능
EOSSessionManager.End(cb);      // 경기 종료 → 다시 참가 가능
EOSSessionManager.Destroy(cb);
```

Lobby와 달리 **로컬 이름**(`LocalSessionName`)으로 조작하며, 이 이름은 다른
플레이어에게 보이지 않습니다. 속성 공개는 `SessionAttributeAdvertisementType.Advertise`
(Lobby의 `Visibility.Public`에 해당).

---

## 6. 동기화 설계 (이 프로젝트 방식)

캐릭터가 클릭 이동이라, **좌표를 매 프레임 보내지 않고 "클릭한 목적지"만 보냅니다.**

```
A가 클릭
  ↓  Move 패킷 브로드캐스트 (9바이트)
B, C가 받은 목적지로 OrderTargetPosition 실행
  ↓
모두가 동일한 회전→보행 로직을 각자 돌림 (애니메이션까지 동일)
```

루트 모션은 프레임레이트에 따라 미세하게 달라지므로, **0.2초마다 소유자가
자기 위치를 보정 전송**합니다. 이 Sync 패킷이 **나중에 입장한 사람에게 현재
위치를 알려주는 역할도 겸해서**, 별도의 입장 핸드셰이크가 필요 없습니다.

| 패킷 | 크기 | 내용 |
|---|---|---|
| `Move` | 9 B | 타입 + 목표 x/z |
| `Sync` | 13 B | 타입 + 위치 x/z + yaw |

보정은 **1.5m 이상 어긋났을 때만 스냅**합니다. 매번 맞추면 원격 캐릭터가 떱니다.

### 소유권 모델

**각자 자기 캐릭터만 조종합니다.** 검증 주체가 없으므로 클라이언트를 조작하면
자기 캐릭터를 자유롭게 움직일 수 있습니다. 협동/캐주얼에는 충분하지만
경쟁전에는 부적합합니다.

### 호출 경로

```
CameraMouseInput.cs:63   localPlayer.OrderMove(hitPosition)    내 클릭 → 전송
NetworkGameManager:229   player.ApplyMove(target)              상대 이동 수신
NetworkGameManager:236   player.ApplySync(position, yaw)       위치 보정 수신
NetworkGameManager:211   player.Initialize(owner, isLocal)     소유권 설정
```

`NetworkPlayer`는 **런타임에 `AddComponent`로 붙입니다.** 씬의 `Character`를
원격 플레이어의 복제 템플릿으로도 쓰기 때문에, 미리 붙여두면 복제본이
`IsLocal = true`를 물려받아 혼란이 생깁니다.

---

## 7. 모바일 빌드 주의사항

### link.xml (IL2CPP 코드 제거 방지)

**에디터에서는 잘 되는데 실기기에서만 죽는** 유형의 문제입니다.

`Assets/EOS/link.xml`은 `Newtonsoft.Json`과 설정 변환 타입들을 보존합니다.
이들은 리플렉션으로만 참조되므로 IL2CPP가 "안 쓰는 코드"로 판단해 제거하고,
그러면 실기기에서 **설정 파싱 실패 → 초기화 불가**가 됩니다.
에디터는 IL2CPP를 안 쓰므로 재현되지 않습니다.

### Android Gradle (AGP 9 호환)

Unity 6.7은 **Gradle 9.1 / AGP 9.0**을 쓰는데, 플러그인은 AGP 3.6 시절 문법으로
파일을 생성합니다. `Assets/Editor/EOSAndroidGradlePatcher.cs`가 이를 보정합니다.

| 문제 | 조치 |
|---|---|
| `jcenter()` | Gradle 9에서 삭제 → `buildscript` 블록 제거 |
| `classpath AGP 3.6.0` | 루트가 AGP 9 제공 → 제거 |
| `compileSdkVersion` | → `compileSdk { version = release(N) }` |
| `targetSdkVersion` (라이브러리) | AGP 9에서 삭제 → 제거 |
| `buildToolsVersion '30.0.3'` | 미설치 → 제거 (AGP 기본값 사용) |
| 매니페스트 `package=` | AGP 8에서 삭제 → `namespace`로 이동 |
| EOS aar의 Java 8+ API | `coreLibraryDesugaring` 추가 |

**패치 위치가 중요합니다.** 플러그인의 `AndroidBuilder.PreBuild`가 매 빌드마다
패키지에서 파일을 덮어쓰므로, `Assets` 쪽 파일을 고쳐도 소용없습니다.
`IPostGenerateGradleAndroidProject`(Gradle 프로젝트 생성 후 · Gradle 실행 전)에서
패치해야 덮어쓰기와 패키지 재해석을 모두 견딥니다.

> 근본적으로는 **Unity 6.3 LTS로 옮기면** AGP 8.x를 쓰므로 이 패처 자체가
> 불필요해집니다.

### Player Settings

| 항목 | 값 |
|---|---|
| Scripting Backend | **IL2CPP** (필수) |
| Min SDK | 24 이상 (현재 26) |
| Package Name | 반드시 자체 ID로 변경 |
| iOS 최소 버전 | 13.0 이상 (현재 15.0) |

---

## 8. 에러 대응표

| 에러 | 원인 |
|---|---|
| `InvalidCredentials` | ClientId/Secret 불일치. **플랫폼별 config 파일 동기화 확인** |
| `AccessDenied` | Client Policy 권한 부족 (`GameClient /w UnlockAchievements` 권장) |
| `InvalidSandboxId` | Sandbox ↔ Deployment 조합 불일치 |
| `NotConfigured` | Platform 미초기화. `EOSManager`가 씬에 있는지 확인 |
| `AlreadyConfigured` | `PlatformInterface.Initialize` 2중 호출 |
| `DuplicateNotAllowed` | **정상** (이미 기기 ID 존재) |
| `InvalidUser` | **정상** (최초 로그인 → `CreateUser`로) |
| 로비를 못 찾음 | 속성 `Visibility`가 `Public`인지 확인 |
| 콜백이 안 옴 | `EOSManager`가 씬에 없어 `Tick()`이 안 돌고 있음 |

### 로그 보는 곳

- **Unity 6.7은 로그를 프로젝트 안으로 옮겼습니다**: `<project>/Logs/Editor.log`
  (기존 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 아님)
- 에디터에서는 EOS SDK 로그가 **자동으로 `VeryVerbose`**입니다
  (`EOSManager.cs`에 `#if UNITY_EDITOR`). Console에서 **`LogEOS`**로 검색하면
  실패 원인이 그대로 나옵니다.

### 도메인 리로드가 꺼져 있을 때

이 프로젝트는 `Enter Play Mode Options`가 켜져 있어 **static 값이 Play 세션 간에
남습니다.** 이전 세션의 로비 ID가 남아 "로비를 만든 적도 없는데 `IsInLobby=True`"가
되는 문제가 있었습니다. 대응:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStatics() { CurrentLobbyId = null; }
```

---

## 9. P2P의 한계 (설계 전에 알아둘 것)

EOS는 P2P의 "배관 문제"는 잘 처리하지만, **구조적 한계는 그대로입니다.**

| 항목 | EOS |
|---|---|
| NAT 통과 / 릴레이 폴백 | ✅ 자동 (STUN/TURN 운영 불필요) |
| 포트포워딩 | ✅ 불필요 |
| IP 은닉 | ⚠️ `ForceRelays` 시에만 (지연 증가) |
| 치팅 방지 | ❌ 전용 서버 없이는 불가 |
| 호스트 이탈 대응 | ❌ 직접 구현 (로비 호스트 이전은 "방장"만 바뀜) |
| 대규모 인원 | ❌ 트래픽 O(N²), **4~8인이 현실적 상한** |
| 패킷 분할 | ❌ 직접 구현 (**최대 1170 바이트**) |
| 지연 균일성 | ❌ 플레이어별 회선 차이 |

`RelayControl` 옵션:

```
NoRelays    = 0   릴레이 안 씀 (제한적 NAT에서 연결 실패)
AllowRelays = 1   직접 연결 실패 시 릴레이  ← 기본값
ForceRelays = 2   항상 릴레이 (IP 숨김, 지연 증가)
```

> **모바일 주의**: 통신사 CGNAT 환경에서는 `NATType`이 대부분 `Strict`라
> 릴레이를 타는 경우가 많습니다. LAN 환경(에디터끼리)에서는 안 드러나므로
> **실기기 + 모바일 회선으로 반드시 확인**해야 합니다.

### 확장이 필요해지면

- `Send()`를 직접 호출하지 말고 한 단계 래핑해두면, 나중에 호스트 집중형이나
  전용 서버로 바꿀 때 게임 코드를 안 건드려도 됩니다
- 본격적인 동기화는 **Netcode for GameObjects + EOS Transport**
  (플러그인에 `P2PNetcodeSample` 포함)

---

## 10. 참고 링크

- [EOS Plugin for Unity (GitHub)](https://github.com/EOS-Contrib/eos_plugin_for_unity)
- [Epic Developer Portal](https://dev.epicgames.com/portal/)
- [EOS SDK Error Codes](https://dev.epicgames.com/docs/epic-online-services/sdk-error-codes)
- [Client / Client Policy 관리](https://dev.epicgames.com/docs/epic-online-services/eos-fundamentals/client-and-client-policy/client-policy-guide)
- [Connect Interface Reference](https://dev.epicgames.com/docs/game-services/connect/connect-reference)
- 플러그인 내 문서: `Library/PackageCache/com.playeveryware.eos@*/Documentation~/`
