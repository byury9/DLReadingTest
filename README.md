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

단축키: `F5` 읽기, `←/→` 이전/다음, `Space` 재생/정지, `Esc` ROI 편집 종료

### 샘플 이미지 (`samples/`)

생산 라인 카메라 이미지를 흉내 낸 합성 이미지 12장입니다. 가운데에 Lot 번호와 유통기한이 인쇄돼 있고
(홀수 번호는 잉크젯 도트 방식, 짝수 번호는 일반 인쇄체), 바깥에는 `PRODUCT ...`, `NET 500G ...` 같은 방해 글자가 있습니다.
가운데 인쇄 영역에만 ROI를 잡으면 방해 글자를 빼고 읽는 걸 확인할 수 있습니다. 정답은 `samples/answers.txt` 에 있습니다.
(`training/make_samples.py` 로 다시 만들 수 있습니다.)

## 빌드 / 실행

1. Visual Studio 2022 (".NET 데스크톱 개발" 워크로드)에서 `DLReadingTest.sln` 열기
2. 빌드하면 NuGet 패키지(`Microsoft.ML.OnnxRuntime`)가 자동 복원되고, `Models\*.onnx` 가 출력 폴더로 복사됨
3. 실행 → `폴더 열기` → `ROI 설정` 으로 글자 영역 지정 → `읽기`

> 64비트 전용(x64)입니다. ONNX Runtime 1.20 이상은 Visual C++ 재배포 패키지(2015-2022 x64)가 필요할 수 있습니다.

## 모델 / 학습 (`training/`)

공개된 "알파벳/숫자 읽기" YOLO 모델이 따로 없어서, 글자 하나하나를 객체(36 클래스)로 검출하도록
**YOLO26n**(Ultralytics, NMS-free, CPU/엣지 추론과 작은 객체에 강함)을 합성 데이터로 학습했습니다.
검출된 글자는 위→아래 줄, 줄 안에서 왼쪽→오른쪽 순서로 이어 붙여 문자열로 만듭니다.

포함된 모델은 **합성 이미지로만 학습한 데모 모델**입니다. 실제 현장 이미지(제품 인쇄, 잉크젯 Lot 번호 등)는
조명, 재질, 폰트가 달라서 인식률이 떨어질 수 있습니다. 현장 이미지를 수십~수백 장 라벨링해서 추가 학습하는 것을 권장합니다.

```bash
cd training
pip install ultralytics onnx onnxslim
python gen_synth.py --out datasets/alnum --train 6000 --val 600           # 합성 데이터 생성
python train.py --data datasets/alnum/data.yaml --epochs 60 --device 0     # GPU 학습 (CPU 는 --device cpu)
```

- 학습이 끝나면 `runs/alnum/weights/best.onnx` 가 `DLReadingTest/Models/alnum_yolo26n.onnx` 로 복사됩니다.
- Windows 에서 `gen_synth.py` 를 실행하면 `C:\Windows\Fonts` 의 폰트를 자동으로 사용합니다.
- 현장 이미지 라벨링: [Label Studio](https://labelstud.io), [CVAT](https://www.cvat.ai) 등에서 YOLO 형식으로 내보내고
  `data.yaml` 의 `train`/`val` 에 경로를 추가하거나, `--weights runs/alnum/weights/best.pt` 로 이어서 학습합니다.
  클래스 순서는 `0-9` 다음 `A-Z` 입니다(클래스 번호 0~35).
- 소문자 등 다른 문자를 추가하려면 `gen_synth.py --charset` 으로 문자 집합을 바꿔서 다시 학습합니다.

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
training/
  gen_synth.py                         합성 학습 데이터 생성기
  train.py                             YOLO26 학습 + ONNX export
```
