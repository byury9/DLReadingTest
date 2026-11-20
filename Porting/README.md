# CognexYoloVerifier (VisionPro 프로젝트 이식용)

기존 `CogPMAlignTool` → `CogFixtureTool` → `CogOCRMaxTool` 흐름에서 **OCRMax 부분만 YOLO 검증으로 바꾸는** 클래스입니다.
PMAlign / Fixture 는 그대로 사용합니다.

```
PMAlign 결과 (점수 순) → 위치 순 정렬 → 박스마다 Fixture + AffineTransform 으로 회전 보정 크롭   (호출 스레드)
                                     → YOLO 검출 + TextVerifier 검증                         (박스별 병렬)
                                     → 결과 / 그래픽 (루트 "@" 좌표)                            (호출 스레드)
```

> 이 폴더는 Cognex DLL 이 필요해서 `DLReadingTest` 솔루션 빌드에는 포함되지 않습니다.

## 복사할 파일

| 파일 | 설명 |
|---|---|
| `DLReadingTest/Yolo/*.cs` | YoloDetector, TextVerifier, Detection 등 (4개) |
| `Porting/CognexYoloVerifier.cs` | 이 클래스 |
| `DLReadingTest/Models/alnum_yolo26n.onnx` | 모델 (출력 폴더로 복사되게 설정) |

- NuGet: `Microsoft.ML.OnnxRuntime` (x64 = 1.32.0, **x86 = 1.22.1**)
- 참조: `Cognex.VisionPro`, `Cognex.VisionPro.Core`, `Cognex.VisionPro.CalibFix`, `Cognex.VisionPro.ImageProcessing`, `Cognex.VisionPro.PMAlign`, `System.Drawing`

## ROI 준비

박스 1개 기준으로 **제조번호 + 날짜 2줄을 모두 포함하는 ROI 하나**를 Fixture 좌표계로 잡습니다.
QuickBuild 에서 편집하기 쉽게 Job 에 `CogAffineTransformTool` 을 하나 추가하고(예: 이름 `YoloRoi`, 입력 = Fixture 출력),
그 툴의 Region 을 ROI 로 쓰는 방법을 권장합니다. 기존 OCRMax 툴의 Region 을 넘겨도 됩니다 (그 경우 1줄만 들어감).

## 사용 예 (기존 OCRMax 블록 대체)

```csharp
// 프로그램 시작 시 1번 (모델 로드)
_yolo = new CognexYoloVerifier(Path.Combine(Application.StartupPath, @"Models\alnum_yolo26n.onnx"));
_yolo.Options.PassScore = 0.10f;   // 검증 기준
_yolo.ExpectedBoxCount = 10;       // 박스 수 (PMAlign ApproximateNumberToFind 도 10 이상으로)

// 검사
cogPMalign.Run();
var roiTool = (CogAffineTransformTool)job.VisionTools["YoloRoi"];

// Fixture 는 내부 전용 툴을 사용 (Job 의 Fixture 를 넘기지 마세요 → 아래 '주의' 참고)
CognexYoloResult yr = _yolo.Run(job.IPOneImageTool.OutputImage, cogPMalign.Results,
                                roiTool.Region, "GWTC054P 2031.07.07");
log.DebugFormat("YOLO {0} 박스 {1}/{2} 크롭 {3}ms 추론 {4}ms", yr.Ok ? "OK" : "NG",
                yr.FoundCount, yr.ExpectedBoxCount, yr.CropMs, yr.InferMs);

foreach (CognexBoxResult b in yr.Boxes)          // Index 순 = 위→아래, 왼쪽→오른쪽
{
    // 기존처럼 줄별 key 로 넣으면 뒤쪽 코드 수정 최소화 (key 이름은 기존 것으로)
    AddResult(result, EnVarKey.Lot,  new VisionResult(b.GetLineText(0)));
    AddResult(result, EnVarKey.Date, new VisionResult(b.GetLineText(1)));

    if (Convert.ToBoolean(job.GraphicLevel & EnVisionGraphicLevel.OCRMAX) && b.Graphics != null)
    {
        var dic = new CogGraphicDictionary();
        dic.Add("YOLO", b.Graphics);              // 이미 "@" 좌표 → Map / ParentFromChildTransform 불필요
        job.Graphics.Add("YOLO" + b.Index, dic);
    }
}

static void AddResult(Dictionary<object, VisionResultList> result, object key, VisionResult v)   // 기존 형식에 맞게
{
    if (result.ContainsKey(key)) result[key].Add(v); else result.Add(key, v);
}
```

## 결과

| 항목 | 내용 |
|---|---|
| `yr.Ok` | 박스를 모두 찾았고 모든 박스 OK |
| `b.Ok` / `b.GetLineOk(n)` | 박스 / 줄 OK (**판정은 문자열 비교 대신 이 값을 쓰세요**) |
| `b.GetLineText(n)` | 줄 표시 문자열. 문제 자리는 `_` (없음), `?` (점수 낮음), 다른 글자면 읽은 글자 |
| `b.Found` / `b.Error` | PMAlign 으로 못 찾은 박스는 `Found=false` 로 **NG 처리되어 뒤에 추가**됨 |
| `b.Verify` | 자리별 상세 (`AllChars`: 기대 글자, 점수, 상태, 크롭 좌표 박스) |
| `b.CropToRoot(x, y)` | 크롭 좌표 → 루트 좌표 변환 (직접 그래픽을 그릴 때) |
| `b.Graphics` | ROI 테두리 (박스 OK 초록 / NG 빨강) + 글자 박스와 `글자 점수` 라벨 (OK 초록, 점수 낮음 노랑, 틀림/없음 빨강, 점선 = 위치 추정) |

## 주의

- **Fixture 좌표계 이름**: 내부 전용 Fixture 를 `"YoloFixture"` 좌표계 이름으로 실행합니다 (`FixtureSpaceName`).
  Job 의 Fixture 와 같은 이름(`"Fixture"` 등)을 쓰면 검사 이미지에 그 좌표계가 **마지막 박스 Pose** 로 남아서,
  같은 좌표계 이름으로 남아 있던 다른 그래픽(예전 OCRMax 결과 등)이 마지막 박스 위치에 그려집니다 (실제 현장에서 확인된 현상).
  그래서 `Run(image, pmResults, roi, text)` 처럼 Fixture 를 넘기지 않는 것을 권장합니다.
- **스레드**: Cognex 툴은 스레드 안전하지 않아 크롭과 그래픽은 `Run` 을 호출한 스레드에서 하고, YOLO 만 병렬로 돌립니다.
  `Run` 은 내부 lock 으로 한 번에 하나씩 실행되므로 카메라가 여러 대면 카메라마다 인스턴스를 만드세요.
  모델은 `new CognexYoloVerifier(sharedDetector)` 로 공유할 수 있습니다 (`InferenceSession.Run` 은 스레드 안전).
- **정렬**: PMAlign 결과는 점수 순이라 위치 순으로 다시 정렬합니다. 같은 행으로 보는 Y 차이는 자동(이웃 박스 간격의 절반)이며,
  필요하면 `RowTolerance` 로 지정합니다. `SortByPosition=false` 면 점수 순 그대로입니다.
- **못 찾은 박스**는 어느 자리인지 알 수 없어서 맨 뒤에 붙습니다. 자리까지 알아야 하면 박스 기준 위치표와 비교하도록 확장하세요.
- **확인 필요 (Cognex 없이 작성)**: 이 저장소에는 VisionPro 가 없어서 VisionPro API 흉내(stub)로 컴파일과 흐름만 확인했습니다
  (현장 이미지 1장, 10박스 OK / 틀린 날짜 NG / PMAlign 8개 → 2개 NG 추가, 그래픽 위치 일치).
  처음 적용할 때 아래 VisionPro API 이름이 버전에 맞는지, 그래픽이 글자 위에 정확히 놓이는지 확인해 주세요.
  - `CogRectangleAffine.Copy(CogCopyShapeConstants.GeometryOnly)`, `CornerOriginX/Y`, `CornerXX/XY`, `CornerYX/YY`
  - `ICogImage.GetTransform("@", space)`, `ICogTransform2D.MapPoint`, `ICogImage.ToBitmap()`
  - `CogPolygon.NumVertices` / `SetVertex`, `CogGraphicLabel.SetXYText`, `CogAffineTransformTool.Dispose`
