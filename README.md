# DLReadingTest

YOLO26 딥러닝 모델로 이미지 속 **영문 대문자(A-Z)와 숫자(0-9)** 를 읽는 C# WinForms 테스트 프로그램입니다.

- .NET Framework 4.8 / C# WinForms (x64)
- 추론: [ONNX Runtime](https://onnxruntime.ai) (`Microsoft.ML.OnnxRuntime`) - OpenCV 불필요
- 모델: Ultralytics **YOLO26n** 을 문자 검출용으로 학습한 ONNX (`DLReadingTest/Models/alnum_yolo26n.onnx`)

## 기능

| 기능 | 설명 |
|---|---|
| 폴더 열기 | 선택한 폴더의 이미지(bmp, jpg, png, tif, gif)를 파일명 순서로 불러옴 |
| 이미지 순환 | `▶ 재생` 시 설정한 간격(ms)마다 다음 이미지로 넘어가고 마지막 다음은 처음으로 돌아옴. `◀ 이전` / `다음 ▶` 으로 수동 이동 |
| ROI 설정 | `ROI 설정` 을 누른 뒤 이미지 위를 드래그해서 영역 지정. 안쪽 드래그는 이동, 테두리/모서리 드래그는 크기 조절. `ROI 해제` 시 전체 영역을 읽음 |
| 읽기 | ROI 영역만 잘라서 YOLO 추론. `자동 읽기` 를 켜면 이미지가 바뀔 때마다 자동으로 읽음 |
| 결과 표시 | 이미지 위에 글자별 박스와 읽은 값을 표시(초록 ≥0.7, 주황 ≥0.5, 빨강 <0.5). 오른쪽에 줄 단위 문자열과 글자별 문자/신뢰도/X/Y/W/H 표. 표의 행을 선택하면 해당 글자가 강조됨 |
| 설정 저장 | 마지막 폴더, ROI, 모델 경로, 간격, 신뢰도를 실행 폴더의 `DLReadingTest.ini` 에 저장 |
| 글자 치환 | 영문 `O` 는 쓰지 않으므로 숫자 `0` 으로 표시 (`DLReadingTest.ini` 의 `LabelMap=O:0`, 여러 개는 `O:0,I:1` 처럼 쉼표로 구분, 비우면 치환 안 함) |

단축키: `F5` 읽기, `←/→` 이전/다음, `Space` 재생/정지, `Esc` ROI 편집 종료

### 샘플 이미지 (`samples/`)

- `real_2025-05-28_10-12-15.bmp`: 실제 카메라 이미지 (박스 10개, 제조번호 `GWTB016P` / 사용기한 `2030.03.12`)
- `sample_01~12.png`: 실제 이미지에서 영숫자 인쇄만 지운 바탕에 다른 값을 찍은 합성 이미지
  - 01~04 정상, 05~08 움직임 번짐(가로로 글자가 끌림), 09~12 인쇄 문질림(획 일부 지워짐/번짐)
  - 정답은 `samples/answers.txt` (`training/make_samples.py` 로 다시 만들 수 있음)

박스 하나의 인쇄 영역에 ROI를 잡으면 한글 라벨(`제조번호 :` 등)을 빼고 그 박스만 읽습니다.
ROI 없이 전체를 읽으면 박스 10개를 모두 읽고, 결과는 줄 단위(왼쪽 박스 → 오른쪽 박스 순)로 나옵니다.

## 빌드 / 실행

1. Visual Studio 2022 (".NET 데스크톱 개발" 워크로드)에서 `DLReadingTest.sln` 열기
2. 빌드하면 NuGet 패키지(`Microsoft.ML.OnnxRuntime`)가 자동 복원되고, `Models\*.onnx` 가 출력 폴더로 복사됨
3. 실행 → `폴더 열기`(예: `samples`) → `ROI 설정` 으로 글자 영역 지정 → `읽기`

> 64비트 전용(x64)입니다. ONNX Runtime 1.20 이상은 Visual C++ 재배포 패키지(2015-2022 x64)가 필요할 수 있습니다.

## 모델 / 학습 (`training/`)

공개된 "알파벳/숫자 읽기" YOLO 모델이 따로 없어서, 글자 하나하나를 객체(36 클래스: `0-9`, `A-Z`)로 검출하도록
**YOLO26n**(Ultralytics, NMS-free, CPU/엣지 추론과 작은 객체에 강함)을 학습했습니다.
검출된 글자는 위→아래 줄, 줄 안에서 왼쪽→오른쪽 순서로 이어 붙여 문자열로 만듭니다.
`.` 은 읽지 않으므로 날짜 `2030.03.12` 는 `2030 03 12` 처럼 나옵니다(넓은 간격에 공백).

### 학습 데이터 (`gen_label.py`)

실제 카메라 이미지에 맞춰 만든 합성 데이터입니다.

- 제조번호는 영문 위주로 만들고, 헷갈리기 쉬운 글자(Q/C/G, I/J, M/W/V/N, P/B/R/F 등)는 3배 자주 넣습니다(날짜 때문에 숫자가 많이 학습되는 것을 보정).
- 영문 `O` 는 만들지 않습니다(숫자 `0` 만 학습). 클래스 번호 36개는 그대로라 기존 모델과 호환됩니다.

| 구성 | 비율 | 내용 |
|---|---|---|
| 실제 바탕 | 55% | `assets/plate_clean.png` (실제 이미지에서 영숫자만 지운 바탕) 20개 인쇄 위치에 무작위 제조번호/날짜를 흰색 단선 글씨, 넓은 자간으로 인쇄 |
| 합성 박스 | 30% | 어두운 박스 + 한글 라벨(`제조번호 :` `사용기한 :` 등, 라벨링 안 함) + 코드. 박스 개수/크기/밝기 다양 |
| 범용 | 15% | `gen_synth.py` 의 다양한 폰트/배경 (일반화용) |

- **움직임 번짐**(40%): 컨베이어 방향(가로)으로 글자 높이의 10~60% 길이만큼 끌림, 가끔 세로/사선으로 짧게
- **인쇄 문질림**(박스별 30%): 긁힌 줄무늬로 획 일부 지워짐, 한쪽으로 번진 꼬리, 잉크 빠짐
- 크롭: 박스 하나 / 이웃 박스 여러 개 / 전체 이미지
- 밝기, 감마, 노이즈, 블러, JPEG 압축

```bash
cd training
pip install ultralytics onnx onnxslim
python gen_label.py --out datasets/label --train 4000 --val 400                      # 데이터 생성
python train.py --data datasets/label/data.yaml --imgsz 640 --epochs 60 --device 0   # GPU 학습 (CPU 는 --device cpu)
```

- 학습이 끝나면 `best.onnx` 가 `DLReadingTest/Models/alnum_yolo26n.onnx` 로 복사됩니다.
- Windows 에서 실행하면 `C:\Windows\Fonts` 의 폰트(맑은 고딕 등)를 자동으로 사용합니다.
- **현장 이미지 추가 학습 (권장)**: 실제 이미지를 수십~수백 장 라벨링하면 인식률이 크게 올라갑니다.
  [Label Studio](https://labelstud.io), [CVAT](https://www.cvat.ai) 등에서 글자마다 박스를 그려 YOLO 형식으로 내보내고
  `data.yaml` 의 `train` 에 폴더를 추가하거나, `--weights <이전 best.pt>` 로 이어서 학습합니다.
  클래스 번호는 `0-9` → 0~9, `A-Z` → 10~35 입니다.
- 다른 제품/인쇄 형태가 생기면 `assets/` 에 새 바탕(인쇄를 지운 이미지)을 추가하는 방식으로 확장할 수 있습니다.

### 현재 포함된 모델의 성능

`Models/alnum_yolo26n.onnx`: YOLO26n, 합성 데이터 4,000장으로 CPU에서 20 epoch 학습(512px), 입력 640px로 export.
검증셋(합성) mAP50 0.61 / mAP50-95 0.46.

| 읽는 방식 | 사용기한(날짜) | 제조번호(영문+숫자) | 속도(CPU) |
|---|---|---|---|
| **박스 하나에 ROI** (실제 이미지 박스 10개) | 10/10 | 4/10 완전 일치, 나머지는 1글자 틀림(`G→C`, `W` 누락, `P→F`) | 약 50 ms |
| 박스 하나에 ROI (샘플 5장 × 10박스) | 48/50 | 15/50 완전 일치, 대부분 1~2글자 틀림 | 약 50 ms |
| ROI 없이 전체 이미지 (실제+샘플 13장 × 10박스) | 121/130 | 4/130 (글자가 작아서 영문을 많이 놓침) | 약 85 ms |

- **ROI를 박스 하나에 잡고 읽는 것을 권장**합니다. 글자가 크게 확대되어 인식률이 확실히 좋아집니다.
- 숫자는 잘 읽고, 영문 중 `W`(검증 mAP50 0.39), `G/C`, `P/B/R`, `T/H` 를 자주 헷갈립니다.
- 움직임 번짐이 심하면 번진 꼬리를 글자로 잘못 검출하기도 합니다(예: `WVWGP7VTY` → `WWWGP7VTY`).
- 개선 방법(효과 큰 순): ① 실제 현장 이미지 라벨링 후 추가 학습, ② GPU에서 640px로 60 epoch 이상 학습,
  ③ `--weights yolo26s.pt` 로 더 큰 모델 사용.

### 다른 모델 사용

`모델 열기` 로 다른 ONNX 를 불러올 수 있습니다. YOLO26(end-to-end 출력 `[1,N,6]`)과
YOLOv8 / YOLO11(출력 `[1,4+클래스,앵커]`) 형식을 자동 판별합니다.
클래스 이름은 Ultralytics 가 ONNX 메타데이터에 넣어 둔 `names` 를 쓰고,
모델과 같은 이름의 `.txt`(한 줄에 클래스 하나)가 있으면 그 파일을 우선 사용합니다.

## 프로젝트 구조

```
DLReadingTest.sln
DLReadingTest/
  MainForm.cs / MainForm.Designer.cs   메인 화면 (폴더, 순환, ROI, 읽기, 결과 표)
  Controls/ImageViewer.cs              이미지 표시 + ROI 편집 + 결과 오버레이 컨트롤
  Yolo/YoloDetector.cs                 ONNX Runtime 추론 (레터박스 전처리, 출력 해석, NMS)
  Yolo/TextAssembler.cs                글자 검출 결과 -> 줄 단위 문자열
  Yolo/Detection.cs
  AppSettings.cs                       ini 설정 저장
  Models/alnum_yolo26n.onnx            학습된 문자 검출 모델
samples/                               테스트 이미지 (실제 1장 + 합성 12장, answers.txt)
training/
  gen_label.py                         제품 박스 인쇄용 학습 데이터 생성기 (쓸림 포함)
  gen_synth.py                         범용 합성 데이터 생성기
  make_samples.py                      samples/ 생성기
  train.py                             YOLO26 학습 + ONNX export
  assets/plate_clean.png               실제 이미지에서 영숫자 인쇄만 지운 바탕
  assets/plate_slots.json              바탕의 인쇄 위치(20줄)
```
