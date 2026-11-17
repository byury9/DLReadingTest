"""
YOLO26 문자 검출 모델 학습 + ONNX export.

사용 예:
    # 1) 합성 데이터 생성
    python gen_synth.py --out datasets/alnum --train 6000 --val 600
    # 2) 학습 (GPU 가 있으면 --device 0)
    python train.py --data datasets/alnum/data.yaml --epochs 60 --imgsz 640 --device 0
    # 3) 결과 ONNX 를 C# 프로그램의 Models 폴더로 복사
    #    runs/alnum/weights/best.onnx -> DLReadingTest/Models/alnum_yolo26n.onnx

실제 현장 이미지를 라벨링해서 data.yaml 에 추가(또는 --weights 로 이어서 학습)하면
인식률이 크게 올라간다. 라벨 형식: 이미지마다 같은 이름의 .txt, 한 줄에
"클래스번호 cx cy w h" (0~1 정규화), 클래스 번호는 data.yaml 의 names 순서.
"""
import argparse
import shutil
from pathlib import Path

from ultralytics import YOLO


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="datasets/alnum/data.yaml")
    ap.add_argument("--weights", default="yolo26n.pt", help="시작 가중치 (yolo26n.pt / yolo26s.pt / 이전 best.pt)")
    ap.add_argument("--epochs", type=int, default=60)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--batch", type=int, default=16)
    ap.add_argument("--device", default="cpu", help="cpu 또는 0 (GPU)")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--name", default="alnum")
    ap.add_argument("--project", default="runs")
    ap.add_argument("--fraction", type=float, default=1.0, help="학습 데이터 사용 비율 (빠른 테스트용)")
    ap.add_argument("--export-imgsz", type=int, default=0, help="ONNX 입력 크기 (0 = 학습 크기와 동일)")
    ap.add_argument("--out", default="../DLReadingTest/Models/alnum_yolo26n.onnx",
                    help="export 된 ONNX 를 복사할 경로")
    args = ap.parse_args()

    model = YOLO(args.weights)
    results = model.train(
        data=args.data,
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=args.device,
        workers=args.workers,
        project=args.project,
        name=args.name,
        exist_ok=True,
        fraction=args.fraction,
        # 문자는 좌우/상하 반전하면 다른 글자가 되므로 반드시 끈다
        fliplr=0.0,
        flipud=0.0,
        # 회전/기울임은 합성 데이터에 이미 들어 있으므로 약하게
        degrees=3.0,
        shear=2.0,
        perspective=0.0002,
        translate=0.1,
        scale=0.5,
        mosaic=1.0,
        close_mosaic=min(10, max(1, args.epochs // 5)),
        hsv_h=0.02,
        hsv_s=0.5,
        hsv_v=0.4,
        patience=30,
        plots=True,
    )

    save_dir = Path(model.trainer.save_dir) if model.trainer else Path(results.save_dir)
    best = save_dir / "weights" / "best.pt"
    print(f"best: {best}")
    export_size = args.export_imgsz or args.imgsz
    onnx_path = YOLO(str(best)).export(format="onnx", imgsz=export_size, simplify=True, dynamic=False)
    print(f"onnx: {onnx_path}")
    if args.out:
        out = Path(args.out)
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy(onnx_path, out)
        print(f"복사: {out.resolve()}")


if __name__ == "__main__":
    main()
