# 제3자 구성요소 고지 (THIRD-PARTY NOTICES)

제3자 구성요소 없음.

몽캡쳐는 외부 라이브러리·패키지를 가져다 쓰지 않으며, 제3자 DLL·폰트·소리 등을 함께 배포하지 않는다. 배포되는 실행 파일(`몽캡쳐.exe`)에는 이 저장소의 소스(`src/`)와 앱 아이콘만 들어 있다.

- 사용하는 것은 Windows에 포함된 .NET Framework 4.x(Windows Forms 포함)와 Windows 10/11의 문자 인식(OCR) 엔진(`Windows.Media.Ocr`)뿐이며, 시스템 구성요소로서 이 프로그램과 함께 배포하지 않는다.
- 빌드할 때 Windows SDK의 메타데이터 파일(`Windows.winmd`)을 참조한다. 이 파일은 컴파일에만 쓰이며 결과물에 포함되지 않는다.
- 자동 업데이트 모듈(`src/MongUpdate.cs`, `src/MongUpdateUi.WinForms.cs`)은 몽 시리즈 공통 모듈로 Bh-Nakh 자체 저작물이며 GPL-3.0 대상이다.
