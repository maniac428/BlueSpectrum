# Blue Spectrum 1.7 · SH-E70 FL

Windows 11 64비트에서 PC 소리에 반응하는 좌우 독립 스펙트럼 표시 앱입니다. Technics SH-E70의 **두 표시창**을 실물 사진과 제조사 서비스 매뉴얼을 참고해 재구성했습니다. 사용자의 요청에 따라 상태 영역과 여백을 줄이고 창과 메뉴를 올블랙으로 만든 배치로, 원기기의 표시창 전체 배치와는 다릅니다. 청색 발광은 사용자가 요청한 색상입니다.

v1.7은 **설정 → 그래프를 그릴 GPU**에서 자동 선택 또는 PC의 실제 그래픽카드를 선택할 수 있습니다. **적용**을 누르면 프로그램을 다시 시작하지 않고 그래프를 그리는 GPU를 바꿉니다. 선택은 실행 파일 옆 설정에 저장합니다.

**실행할 때마다 감상 모드로 시작**하는 v1.6 동작을 유지합니다. 처음 표시되는 순간부터 위쪽 메뉴와 하단 상태 영역을 숨기며, 창 위치와 크기는 기존 설정을 복원합니다. **Esc**로 메뉴와 하단을 다시 표시하고 **Ctrl+M**으로 감상 모드를 전환할 수 있습니다. 표시창을 우클릭하면 설정·종료 등의 메뉴를 사용할 수 있습니다.

v1.5에서 추가한 설정 상단의 **화면 정중앙·좌측 위·좌측 하단·오른쪽 위·오른쪽 아래** 창 이동 버튼도 유지합니다. 누르면 분석기 창이 즉시 이동하고 위치를 저장합니다.

화면은 **메뉴 버튼과 창의 경계를 없앤 순수 검정(#000000)**입니다. 표시창과 메인 창의 외곽선, 메뉴 구분선, 유광 반사선과 그라데이션을 제거했습니다. 메뉴·설정·장치 목록·도움말은 마우스를 올리거나 눌러도 검정 배경을 유지하며, 글자 밝기로 조작 상태를 구분합니다. 설정 창의 제목 영역도 검정입니다. 체크 표시, 슬라이더 손잡이·트랙, 스크롤바 등 기능을 식별하는 조작부는 회색으로 표시합니다.

이전 버전에서 줄인 왼쪽 공백과 오른쪽 상태 영역 없는 배치를 유지합니다. MEMO·EQ PLUS·REVERSE, 숫자 1~6, EQ ON·FLAT 영역은 표시하지 않습니다. 기본 창 크기 800×214, 주파수 막대와 FULL RANGE, 오디오 분석 방식과 표시 설정은 v1.3과 같습니다.

## 실행

**`BlueSpectrum-SH-E70-v1.7-Windows-x64.zip`**의 압축을 쓰기 가능한 폴더에 풀고 **BlueSpectrum.exe**를 더블클릭하세요. 작업 폴더에서는 `publish-v1.7/BlueSpectrum.exe`를 실행합니다. 배포본은 .NET 실행 환경을 포함하므로 개발 도구나 가상 오디오 케이블을 설치할 필요가 없습니다. 처음 실행할 때 구성 요소를 사용자 임시 폴더에 풉니다. 설정은 실행 파일 옆 `settings.json`에 저장합니다. 이전 설정 파일을 함께 사용하면 저장된 창 위치·크기와 표시 설정을 이어서 사용합니다. 저장 위치가 화면 범위를 벗어나면 중앙에서 시작합니다.

현재 기본 출력 장치에서 재생되는 소리를 자동으로 표시합니다. 소리를 다른 장치로 보내는 앱은 **우클릭 → 설정 → 출력 장치**에서 그 장치를 선택하세요. 선택 장치 이름은 **Esc**로 하단을 표시한 뒤 상태 위에 마우스를 올리면 보입니다.

## 사용

- **설정 → 창 위치**: 화면 정중앙 또는 네 모서리로 즉시 이동합니다.
- **설정 → 그래프를 그릴 GPU → 적용**: 자동 선택 또는 표시된 그래픽카드로 즉시 전환합니다.
- **설정**: 출력 장치, 표시 감도, 형광 밝기, 빛 번짐, 반응·잔상, 피크 홀드, 30/60 FPS.
- **항상 위** / `Ctrl+T`: 다른 창 위에 표시합니다.
- **기본 시작 화면은 감상 모드**입니다. **감상** / `Ctrl+M`으로 위·아래 도구 영역의 표시를 전환합니다. 표시창을 드래그해 이동하고 가장자리로 크기를 조절합니다.
- **전체 화면** / `F11`: 전체 화면을 전환합니다.
- **Esc**: 일반 화면으로 돌아옵니다. 우클릭 메뉴로도 설정·복귀·종료할 수 있습니다.
- **표시 감도**는 화면만 조절합니다. 듣는 소리와 PC 볼륨은 바뀌지 않습니다.

창 위치 버튼은 왼쪽에 좌측 위·하단, 가운데에 화면 정중앙, 오른쪽에 오른쪽 위·아래를 배치했습니다. **분석기 창이 있는 모니터의 작업 표시줄을 제외한 영역**을 기준으로 이동하며 창 크기는 유지합니다. 전체 화면이나 최대화 상태에서는 일반 창으로 복원한 뒤 이동합니다. 감상 모드는 유지됩니다.

위치 버튼을 눌러도 설정 창은 열린 상태로 남습니다. **창 위치는 즉시 저장되므로 이후 취소를 눌러도 유지**됩니다. GPU, 출력 장치와 표시 감도 등 나머지 옵션은 **적용** 버튼을 눌러 반영하세요. 설정 창 미리보기는 `artifacts/options-v1.7.png`입니다.

표시가 작으면 감도를 +6 → +12dB 정도로 높여 보세요. 좌우 감도는 함께 적용되므로 채널 간 크기 차이는 유지됩니다. 기본 창 크기는 800×214, 최소 크기는 720×200이며 피크 홀드는 기본으로 꺼져 있습니다. 기존 설정 파일을 사용하는 경우 저장된 설정이 적용될 수 있습니다. 최소화하면 화면 갱신을 줄입니다.

30/60 FPS는 **목표 갱신률**이며 실측 FPS가 아닙니다. 120 FPS 옵션은 없습니다. 모니터와 PC 환경에 따른 실제 유지 여부는 별도 측정이 필요합니다.

## 그래프 GPU 선택

우클릭으로 설정을 열고 **그래프를 그릴 GPU**를 선택한 뒤 **적용**을 누르세요. 이 PC에서는 `NVIDIA GeForce RTX 3070`과 `AMD Radeon(TM) Graphics`가 확인되었으며, 다른 PC에는 그 PC에서 인식한 그래픽카드 이름이 표시됩니다. 설정을 다시 열면 현재 그래프를 그리는 GPU 또는 대체 표시 상태를 확인할 수 있습니다.

기본값인 **자동 선택**은 그래프 그리기를 시작할 때 현재 창의 모니터에 연결된 GPU를 우선 사용하고, 대응하는 출력을 찾지 못하면 열거된 첫 하드웨어 GPU를 사용합니다. 모니터 케이블이 연결되지 않은 그래픽카드도 Windows에서 사용할 수 있는 하드웨어로 인식하면 목록에 포함됩니다. 선택한 카드가 없어졌을 때는 자동 선택으로 대체하고 그 이유와 실제 사용 중인 GPU를 표시합니다. GPU 초기화나 그리기에 실패하면 기존 WPF 표시로 대체하고 연결을 재시도합니다.

**GPU 선택은 그래프를 그리는 장치에 적용됩니다.** 소리 분석과 FFT는 계속 CPU에서 수행합니다. 창·설정 화면과 Windows 바탕화면을 최종 합성하는 GPU까지 강제로 바꾸는 기능은 아닙니다. 바뀌지 않는 글자와 눈금은 WPF에서 이미지로 만들어 재사용하고, 움직이는 막대는 선택한 Direct3D 11 장치의 Direct2D로 그립니다. 크기나 배율 등이 바뀌면 고정 이미지도 다시 만듭니다.

GPU를 지정해도 FPS나 전력 효율이 반드시 좋아지지는 않습니다. 선택한 GPU와 화면 출력 GPU가 다르면 추가 전달 비용이 생길 수 있습니다. v1.7에서 실제 FPS·소비전력·CPU/GPU 사용률의 비교 측정은 하지 않았습니다.

## 확인해야 할 차이

참고 모델은 **SH-E70**이며 SH-GE70과 구분합니다. v1.7은 다음 표시 구조를 유지합니다.

- 채널마다 **63, 160, 400Hz, 1, 2.5, 6.3, 16kHz + FULL RANGE**의 8개 막대.
- **13쌍의 가는 가로선**. 서비스 매뉴얼 표시관 도면의 a~m 막대 세그먼트와 실물 사진에 맞춰 재구성했습니다.
- 좌측 **±12 EQ 눈금**과 우측 **0~36 상대 표시 눈금**, 주황·적색 눈금 표시. 왼쪽 공백만 줄였으며 EQ 눈금은 남아 있습니다.

좌측 EQ 눈금은 **외관을 재현한 장식 표시**입니다. 앱은 입력 오디오에 EQ나 음색 가공을 적용하지 않습니다. 오른쪽 상태 영역 삭제와 여백 축소는 사용자 지정 배치이며, 픽셀 단위로 완전히 같은 복제품이나 원본 아날로그 회로와 교정값이 같은 계측기가 아닙니다. 반응 시간·잔광·필터·피크 홀드는 소프트웨어 구현값이며 제조사와 관계없는 개인용 재현입니다.

막대 높이는 내부 RMS dBFS와 공통 표시 감도로 계산합니다. 높이 비율은 `clamp((dBFS + 표시 감도 + 48) / 36, 0, 1)`입니다. 따라서 표시 감도 0dB에서는 내부 -48dBFS가 바닥, -12dBFS가 꼭대기입니다. 우측 **0~36 스킨 눈금을 dBFS나 원기기의 전압 기준 눈금으로 읽으면 안 됩니다.** 좌측 ±12 눈금도 실제 EQ 보정량을 나타내지 않습니다.

내부 dBFS는 디지털 진폭 1을 기준으로 하며, 진폭 1인 정현파의 RMS 값은 약 -3.01dBFS입니다. AES17의 정현파 0dBFS 관례나 음압(dB SPL)과 다릅니다. **FULL RANGE는 채널별 전체 DC~Nyquist 범위의 Hann 창을 적용한 RMS**이며, 나머지 7개 대역 막대의 높이를 합산한 값이 아닙니다. 기존 7개 대역의 분석 방식은 유지합니다.

스테레오는 좌우를 독립 분석합니다. **모노 출력은 동일 신호를 두 화면에 표시**하며 상태에 명시합니다. **5.1/7.1은 전면 좌우(FL/FR)만 표시**하므로 센터 대사나 후면 소리는 그래프에 포함되지 않습니다. 채널 배치가 명시되지 않은 다채널 장치는 첫 두 채널을 쓰고 상태에 알립니다.

## 소리가 있는데 움직이지 않을 때

1. 설정에서 실제 음악이 나오는 출력 장치를 선택합니다.
2. 독점 모드·ASIO·보호된 콘텐츠 등은 일반 WASAPI 공유 루프백으로 캡처되지 않을 수 있습니다. 해당 플레이어의 일반 Windows 공유 출력을 사용해 확인하세요.
3. 끊긴 장치를 직접 선택해 두었다면 그 장치가 돌아올 때까지 기다립니다. 자동 전환을 원하면 시스템 기본 장치를 선택하세요.

무음만으로 보호 콘텐츠인지 판정할 수 없으므로 앱은 이를 단정하지 않습니다. 장치 오류가 있으면 상태에 이유를 표시합니다. 기본 장치는 약 1초 간격으로 확인하고, 절전 복귀 시 연결을 다시 시작합니다.

## 처리와 개인정보

WASAPI loopback → 실제 포맷 디코딩 → 좌우 분리 → 별도 분석 작업 → 화면 그리기로 처리합니다. FFT 4096, hop 1024, Hann 창, 기하평균으로 나눈 주파수 경계와 대역 전력 적분을 사용합니다. 48kHz에서 분석 창은 약85.3ms입니다. 이 값은 전체 화면 반응 지연의 실측값이 아닙니다.

앱은 소리를 메모리에서 분석하며 녹음 파일·클라우드 전송·마이크 입력·오디오 재출력을 사용하지 않습니다. 진단 보고서에도 오디오 원음은 저장하지 않습니다. `--loopback-test` 개발 검증 명령만 낮은 음량의 시험음을 재생합니다.

## 개발과 검증

C# / .NET10 WPF / NAudio.Wasapi 3.1.0 / Direct3D 11·Direct2D (Vortice 3.8.3). SDK10.0.401을 준비한 다음 PowerShell에서 `./build.ps1 -Test -Publish`를 실행합니다. 의존성은 `packages.lock.json`에 고정되어 있습니다. 이 빌드 명령의 결과는 `publish` 폴더에 생성되며, 제공하는 v1.7 배포 실행 파일은 `publish-v1.7`에 있습니다. 소스에는 개발 도구와 패키지 캐시를 포함하지 않습니다. 의존성 원본 라이선스는 `licenses`와 `THIRD-PARTY-NOTICES.md`에 포함합니다.

검증 명령(소스 빌드 또는 실행 파일 뒤에 전달):

```text
BlueSpectrum.exe --self-test artifacts/self-test.json
BlueSpectrum.exe --probe artifacts/capture-probe.json
BlueSpectrum.exe --render artifacts/preview-v1.7.png 800 214
BlueSpectrum.exe --ui-test artifacts/ui-test.json
BlueSpectrum.exe --gpu-test artifacts/v1.7-gpu-test.json
BlueSpectrum.exe --device-test artifacts/device-test.json
BlueSpectrum.exe --loopback-test artifacts/loopback-test.json
```

`--self-test`는 합성 입력 검증입니다. `--probe`는 실제 출력 장치를 읽기만 합니다. `--render`는 실제 분석기에 넣은 합성 신호로 만든 WPF 표시 검증 이미지이며 GPU 선택 검증은 아닙니다. 합성 입력이라는 하단 안내를 보이기 위해 일반 화면으로 복원한 뒤 렌더하므로, **메뉴와 하단이 숨겨지는 실제 시작 화면과 다릅니다.** `--ui-test`는 창과 설정 조작을 검증합니다. **`--gpu-test`는 실제 하드웨어 GPU마다 장치를 만들고, 선택한 GPU와 실제 장치의 일치·합성 좌우 신호의 그리기 결과·실행 중 GPU 전환·크기 변경·대체 처리를 확인**합니다. 이 명령이 만드는 `gpu-*-v1.7.png`는 GPU 그리기 결과를 읽어 저장한 합성 입력 이미지입니다. `--loopback-test`는 기본 출력으로 약5초간 작은 시험음을 재생하며, 다른 소리가 섞이면 결과를 신뢰할 수 없습니다. 실제 수행 결과와 미검증 범위는 `docs/검증결과.md`에 정리합니다. 이전 버전의 과거 통과 기록을 v1.7 재검증 결과로 간주하지 않습니다.

## 참고 근거

- [SH-E70 제조사 서비스 매뉴얼 사본](https://www.manualslib.com/manual/4098885/Technics-Sh-E70.html)
- [서비스 매뉴얼 Page 7: IC51/52의 13개 레벨 출력](https://www.manualslib.com/manual/4098885/Technics-Sh-E70.html?page=7)
- [서비스 매뉴얼 Page 16: FL 표시관 도면](https://www.manualslib.com/manual/4098885/Technics-Sh-E70.html?page=16)
- [SH-E70 실물 근접 사진: Yahoo 경매 기록 사본](https://yahoo.aleado.com/lot?auctionID=d1147066728) — 표시 구조를 사진으로 확인한 보조 근거이며 판매자의 설명이나 사진 색상을 공식 사양으로 취급하지 않습니다.
- [Microsoft WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
- [Microsoft 채널 마스크 규칙](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ksmedia/ns-ksmedia-waveformatextensible)
- [Microsoft MonitorFromWindow: 현재 창의 모니터 선택](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow)
- [Microsoft MONITORINFO: 작업 영역과 음수 화면 좌표](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo)
- [Microsoft D3D11CreateDevice: 지정한 어댑터에서 장치 생성](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-d3d11createdevice)
- [Microsoft DXGI EnumAdapters1: 출력 연결 유무와 관계없이 GPU 열거](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgifactory1-enumadapters1)
- [Microsoft Direct2D·Direct3D 연동](https://learn.microsoft.com/en-us/windows/win32/direct2d/direct2d-and-direct3d-interoperation-overview)
- [NAudio WasapiRecorder 공식 문서](https://naudio.github.io/NAudio/docs/WasapiRecorder.html)
- [NAudio.Wasapi 3.1.0](https://www.nuget.org/packages/NAudio.Wasapi/3.1.0)
