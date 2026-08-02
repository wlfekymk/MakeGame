# MakeGame

1인 인디 게임 개발 프로젝트.

## 1. Unity 설치 (아직 안 했다면)

1. [Unity Hub](https://unity.com/download) 설치
2. Unity Hub에서 최신 **LTS 버전**(예: 2022 LTS 또는 6 LTS) 설치
   - 설치 시 모듈에서 타깃 빌드 플랫폼(Windows Build Support 등) 체크
3. Unity Hub → **New Project** 클릭
   - Template: **Universal 2D** 또는 **Universal 3D** (장르 정해지면 선택, URP 기반)
   - Project name: `MakeGame`
   - **Location: 이 폴더(`D:\MakeGame`)의 상위 폴더로 지정하고, 생성 후 `Assets`, `Packages`, `ProjectSettings` 내용을 이 폴더로 병합**
     (또는 Location을 바로 `D:\MakeGame`으로 지정 — 이미 있는 `Assets/_Project`, `.gitignore` 등과 자동으로 합쳐짐)

Unity가 프로젝트를 생성하면 `Library/`, `Packages/`, `ProjectSettings/`, `Temp/` 등이 자동으로 채워집니다. `Library/`, `Temp/` 등은 `.gitignore`에 이미 등록되어 있어 커밋되지 않습니다.

## 2. 폴더 구조

```
Assets/
  _Project/          # 우리 프로젝트 전용 에셋 (Unity 기본 폴더와 구분)
    Scripts/
      Player/
      Enemy/
      Systems/       # 게임 로직/규칙
      UI/
      Managers/      # GameManager, SceneLoader 등 싱글턴류
      Utils/
    Scenes/
    Prefabs/
    Art/
      Sprites/
      Models/
      Animations/
    Audio/
      SFX/
      Music/
    ScriptableObjects/
    Resources/       # Resources.Load가 꼭 필요한 경우만 사용
  Settings/          # URP Pipeline Asset, Input Actions 등 프로젝트 설정 에셋
```

- `_Project` 접두사(`_`)는 Unity Editor에서 폴더를 맨 위로 정렬시켜 서드파티 에셋과 구분하기 위함입니다.
- 새 스크립트는 장르가 정해지면 해당 하위 폴더(Player/Enemy/Systems 등)에 배치하세요.

## 3. 코드 컨벤션

- 기능을 구현하는 모든 메서드에는 **어떤 기능인지 설명하는 주석**을 남깁니다.

```csharp
/// <summary>
/// 플레이어 입력을 받아 이동 벡터를 계산하고 Rigidbody에 적용한다.
/// </summary>
private void Move(Vector2 input)
{
    ...
}
```

## 4. Git 워크플로

- `main` 브랜치에 직접 커밋하거나, 기능 단위로 브랜치를 나눠 작업 후 병합
- Unity 에디터에서 **Edit > Project Settings > Editor > Version Control Mode**를 `Visible Meta Files`로, **Asset Serialization**을 `Force Text`로 설정해야 `.meta` 파일 충돌과 diff가 정상 동작합니다 (Unity Hub로 새 프로젝트 생성 직후 꼭 설정)
- 씬/프리팹 동시 편집 시 충돌이 잦으니 가능하면 한 사람(1인 개발이므로 크게 문제 없음)이 순차 작업

## 5. 다음 단계

- 게임 장르가 정해지면 `Assets/_Project/Scripts` 하위에 코어 시스템부터 설계
- 필요 시 Input System, 카메라 등 초기 스크립트 요청
