# 에이전: 멈춰버린 행성의 엔지니어

자동화설비기능사 지식을 수리 퍼즐로 푸는 Unity 3D 서바이벌 게임.

- Unity 6.3 LTS (6000.3.x), 내장 렌더 파이프라인, 구형 Input Manager
- `main` 브랜치에 push 하면 GitHub Actions(GameCI)가 WebGL로 빌드해 `gh-pages` 브랜치로 배포합니다.
- 배포 주소: `https://<깃허브아이디>.github.io/<저장소이름>/`
- 씬·데이터·프리팹은 저장소에 없습니다. 빌드할 때 `Assets/Scripts/Editor/CiBuild.cs`가 자동으로 만듭니다.
- 3D 모델은 `Assets/Kenney/` 아래에 Kenney 팩(CC0)의 FBX 폴더를 넣으면 자동으로 쓰이고, 없으면 도형으로 대체됩니다.
- 한글 글꼴(예: NanumGothic-Regular.ttf)을 `Assets/Fonts/`에 넣어야 WebGL에서 한글이 보입니다.

자세한 설정은 CLOUD_BUILD_GUIDE 문서를 보세요.
