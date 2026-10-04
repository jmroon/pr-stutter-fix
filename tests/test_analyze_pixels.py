import importlib.util
import unittest
import csv
import json
import tempfile
from pathlib import Path
import numpy as np

spec=importlib.util.spec_from_file_location('pixels',Path(__file__).resolve().parents[1]/'scripts/analyze_pixels.py')
pixels=importlib.util.module_from_spec(spec);spec.loader.exec_module(pixels)

class PixelTrackingTests(unittest.TestCase):
    def setUp(self):
        self.image=np.random.default_rng(17).integers(0,256,(64,128,4),dtype=np.uint8)

    def test_signed_translation_and_standing_still(self):
        for dx,dy in [(0,0),(5,0),(-8,0),(0,6),(0,-5)]:
            with self.subTest(dx=dx,dy=dy):
                shifted=np.roll(self.image,(dy,dx),axis=(0,1))
                result=pixels.track(self.image,shifted)
                self.assertTrue(result['valid']);self.assertEqual((result['dx'],result['dy']),(dx,dy))

    def test_blank_and_repeating_texture_rejected(self):
        blank=np.full_like(self.image,90)
        self.assertFalse(pixels.track(blank,blank)['valid'])
        repeated=np.tile(self.image[:,:4],(1,32,1))
        self.assertEqual(pixels.track(repeated,repeated)['reason'],'ambiguous_pattern')

    def test_unrelated_frames_and_boundary_rejected(self):
        different=np.random.default_rng(19).integers(0,256,self.image.shape,dtype=np.uint8)
        self.assertFalse(pixels.track(self.image,different)['valid'])
        self.assertEqual(pixels.track(self.image,np.roll(self.image,16,axis=1))['reason'],'search_boundary')

    def test_diagonal_motion_rejected(self):
        self.assertFalse(pixels.track(self.image,np.roll(self.image,(4,5),axis=(0,1)))['valid'])

    def test_capture_layout_scaling_sign_and_truncation(self):
        root=Path(__file__).resolve().parents[1]/'artifacts/tests'
        root.mkdir(parents=True,exist_ok=True)
        with tempfile.TemporaryDirectory(dir=root) as tmp:
            p=Path(tmp);n=5
            base=np.random.default_rng(71).integers(0,256,(64,256,4),dtype=np.uint8)
            sequence=np.stack([np.concatenate([np.roll(base[:,:128],3*i,axis=1),np.roll(base[:,128:],3*i,axis=1)],axis=1) for i in range(n)])
            (p/'patches.rgba').write_bytes(sequence.tobytes())
            meta=dict(count=n,patchHeight=64,packedWidth=256,patchWidth=128,sourceWidth=1280,sourceHeight=720,
                      screenWidth=2560,screenHeight=1440,qpcFrequency=120000,limitation='Synthetic integration fixture')
            (p/'capture.json').write_text(json.dumps(meta))
            with (p/'motion.csv').open('w',newline='') as out:
                writer=csv.DictWriter(out,fieldnames=['frame','qpc','camera_x','camera_y','render_x','render_y','candidate_x','candidate_y','read_ticks','observer_ticks'])
                writer.writeheader()
                for i in range(n):writer.writerow(dict(frame=i,qpc=i*1000,camera_x=-i,camera_y=0,render_x=.25*i,render_y=0,candidate_x=-.75*i,candidate_y=0,read_ticks=120,observer_ticks=150))
            report=pixels.analyze(p)
            self.assertEqual(report['valid_pairs'],4)
            self.assertEqual(report['valid_moving_pairs'],4)
            self.assertEqual(report['readback_median_ms'],1)
            for pair in report['pairs']:
                self.assertEqual(pair['measured_dx'],6)
                self.assertEqual(pair['render_dx'],6)
            self.assertTrue((p/'patch-preview.png').exists())
            (p/'patches.rgba').write_bytes(b'bad')
            with self.assertRaises(ValueError):pixels.analyze(p)

    def test_native_scroll_is_included_when_render_camera_stays_fixed(self):
        a=dict(camera_x=10,camera_y=4,render_x=0,render_y=0,candidate_x=10,candidate_y=4)
        b=dict(a,camera_x=11,candidate_x=11)
        result=pixels.motion_prediction(a,b,2560,1440)
        self.assertEqual(result['render_dx'],-8)
        self.assertEqual(result['render_dy'],0)

    def test_tile_end_uses_only_remaining_time(self):
        a=dict(move_duration='.2',move_timer='.199',start_x='0',start_y='0',dest_x='16',dest_y='0',candidate_x='15.92',candidate_y='0')
        b=dict(a,move_duration='0',move_timer='0',delta_time='.008',frame='25',qpc='1000',candidate_x='16')
        event=pixels.completion_event(a,b,1,2560,1440)
        self.assertAlmostEqual(event['unused_ms'],7)
        self.assertAlmostEqual(event['full_frame_dx'],-5.12)
        self.assertAlmostEqual(event['observed_dx'],-.64)
        b['move_timer']='.01'
        self.assertIsNone(pixels.completion_event(a,b,1,2560,1440))

if __name__=='__main__': unittest.main()
