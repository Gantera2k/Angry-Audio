        // ====== SPECTACULAR UNICORN ======
        static void PaintUnicorn(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade)
        {
            float cx = ev.X * w + ev.DirX * t * w * 1.4f, cy = ev.Y * h + ev.DirY * t * h * 1.4f;
            int a = (int)(220 * fade); float gallop = (float)Math.Sin(t * 60) * 3.5f;
            Color[] rb = { Color.FromArgb(255,50,50), Color.FromArgb(255,140,0), Color.FromArgb(255,240,50),
                Color.FromArgb(50,255,80), Color.FromArgb(50,150,255), Color.FromArgb(160,50,255) };
            // LONG sparkly rainbow trail — 30 segments x 6 bands
            for (int i = 0; i < 6; i++) for (int seg = 0; seg < 30; seg++) {
                float dist = (seg+1)*6f; float sx = cx - ev.DirX*dist;
                float sy = cy - ev.DirY*dist + (i-2.5f)*(2.5f+seg*0.08f) + (float)Math.Sin(t*35+seg*0.4f+i*1.1f)*(1.5f+seg*0.06f);
                int sa = (int)(a*0.45f*(1f-seg/32f)); if(sa<=0) continue;
                float sz = 2.5f+(float)Math.Sin(t*50+seg+i)*0.5f;
                using(var b=new SolidBrush(Color.FromArgb(sa,rb[i].R,rb[i].G,rb[i].B))) g.FillEllipse(b,sx-sz,sy-sz*0.4f,sz*2,sz*0.8f);
            }
            // SPARKLE FIELD — magic particles everywhere
            var sr = new Random(ev.Seed+(int)(t*200));
            for (int sp = 0; sp < 40; sp++) {
                float dist=(float)sr.NextDouble()*180f, spread=(float)(sr.NextDouble()-0.5)*30f;
                float spx=cx-ev.DirX*dist+(float)Math.Sin(t*30+sp)*spread*0.3f;
                float spy=cy-ev.DirY*dist+spread+(float)Math.Cos(t*25+sp*0.7f)*3f;
                float tw=(float)(0.3+0.7*Math.Sin(t*80+sp*2.1));
                int spa=(int)(a*0.6f*tw*(1f-dist/200f)); if(spa<=2) continue;
                Color sc=rb[sp%6]; float ssz=1.2f+tw*1.5f;
                using(var p=new Pen(Color.FromArgb(spa,sc.R,sc.G,sc.B),0.6f)){g.DrawLine(p,spx-ssz,spy,spx+ssz,spy);g.DrawLine(p,spx,spy-ssz,spx,spy+ssz);}
                using(var b=new SolidBrush(Color.FromArgb(spa/2,255,255,255))) g.FillEllipse(b,spx-0.8f,spy-0.8f,1.6f,1.6f);
            }
            cy += gallop;
            // Ambient glow
            for(int gr=3;gr>=1;gr--){float gs=8+gr*6;using(var b=new SolidBrush(Color.FromArgb((int)(a*0.06f*gr),200,180,255))) g.FillEllipse(b,cx-gs,cy-gs*0.6f,gs*2,gs*1.2f);}
            // Body
            using(var b=new SolidBrush(Color.FromArgb(a,245,240,255))) g.FillEllipse(b,cx-7,cy-3.5f,14,7);
            using(var b=new SolidBrush(Color.FromArgb(a/3,255,255,255))) g.FillEllipse(b,cx-4,cy-3,8,3);
            float dir=ev.DirX>0?1:-1;
            // Head
            float hx=cx+dir*7,hy=cy-3;
            using(var b=new SolidBrush(Color.FromArgb(a,248,245,255))) g.FillEllipse(b,hx-3.5f,hy-3.5f,7,7);
            using(var b=new SolidBrush(Color.FromArgb(a,80,50,140))) g.FillEllipse(b,hx+dir*1.5f-0.8f,hy-0.5f,1.6f,1.6f);
            // GOLDEN HORN with sparkle burst
            float htx=hx+dir*5,hty=hy-7;
            using(var p=new Pen(Color.FromArgb(a,255,215,80),1.8f)){p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;g.DrawLine(p,hx+dir*1.5f,hy-2,htx,hty);}
            using(var p=new Pen(Color.FromArgb(a/2,255,255,200),0.8f)) g.DrawLine(p,hx+dir*2,hy-2.5f,htx,hty);
            for(int hs=0;hs<6;hs++){float ang=(float)(hs*Math.PI/3+t*40),hr=3f+(float)Math.Sin(t*70+hs)*2f;
                int hsa=(int)(a*(0.3f+0.5f*(float)Math.Sin(t*80+hs*1.5f)));
                using(var b=new SolidBrush(Color.FromArgb(hsa,255,255,180))) g.FillEllipse(b,htx+(float)Math.Cos(ang)*hr-1,hty+(float)Math.Sin(ang)*hr-1,2,2);}
            // Legs
            for(int leg=0;leg<4;leg++){float lx=cx-4+leg*2.8f,phase=(float)Math.Sin(t*60+leg*1.8f)*3;
                using(var p=new Pen(Color.FromArgb(a*3/4,230,225,245),1f)) g.DrawLine(p,lx,cy+3,lx+phase*0.3f,cy+7+phase);
                if(phase<-1.5f) using(var b=new SolidBrush(Color.FromArgb(a/3,255,215,80))) g.FillEllipse(b,lx-1,cy+7+phase-1,2,2);}
            // Rainbow mane
            for(int m=0;m<8;m++){float mx=cx+(dir>0?-3:3)-ev.DirX*m*2.5f,my=cy-4+(float)Math.Sin(t*45+m*0.9f)*(2+m*0.3f);
                int ma=(int)(a*0.5f*(1f-m*0.08f));float ms=2.5f+(float)Math.Sin(t*55+m)*0.5f;
                using(var b=new SolidBrush(Color.FromArgb(ma,rb[m%6].R,rb[m%6].G,rb[m%6].B))) g.FillEllipse(b,mx-ms,my-ms*0.5f,ms*2,ms);}
            // Rainbow tail
            float tb=cx-dir*8;
            for(int ti=0;ti<12;ti++){float ttx=tb-ev.DirX*ti*3,tty=cy-1+(float)Math.Sin(t*40+ti*0.7f)*(2+ti*0.4f);
                int ta=(int)(a*0.4f*(1f-ti*0.06f));float ts=2f+ti*0.15f;
                using(var b=new SolidBrush(Color.FromArgb(ta,rb[ti%6].R,rb[ti%6].G,rb[ti%6].B))) g.FillEllipse(b,ttx-ts,tty-ts*0.4f,ts*2,ts*0.8f);}
        }

        // ====== WAVE 3: 20 NEW EVENTS ======
        static void PaintStargate(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(180*fade);float op=t<0.2f?t/0.2f:t>0.8f?(1-t)/0.2f:1f;float r=15*op;
            for(int ring=0;ring<5;ring++){float rr=r+ring*4;using(var p=new Pen(Color.FromArgb((int)(a*(0.6f-ring*0.1f)),100,180,255),1.2f)) g.DrawEllipse(p,cx-rr,cy-rr*0.4f,rr*2,rr*0.8f);}
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.15f*op),120,200,255))) g.FillEllipse(b,cx-r,cy-r*0.4f,r*2,r*0.8f);
            var sr=new Random(ev.Seed+(int)(t*100));for(int sp=0;sp<8;sp++){float sa=(float)(sr.NextDouble()*Math.PI*2),sd=r+(float)sr.NextDouble()*6;
                using(var b=new SolidBrush(Color.FromArgb((int)(a*0.5f),200,230,255))) g.FillEllipse(b,cx+(float)Math.Cos(sa+t*20)*sd-1,cy+(float)Math.Sin(sa+t*20)*sd*0.4f-1,2,2);}
        }
        static void PaintTimeTraveler(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(160*fade);
            using(var p=new Pen(Color.FromArgb(a,180,160,100),1f)) g.DrawEllipse(p,cx-5,cy-5,10,10);
            using(var p=new Pen(Color.FromArgb(a,220,200,120),0.8f)){g.DrawLine(p,cx,cy,cx+(float)Math.Cos(t*200)*3,cy+(float)Math.Sin(t*200)*3);g.DrawLine(p,cx,cy,cx+(float)Math.Cos(t*30)*4,cy+(float)Math.Sin(t*30)*4);}
            for(int i=0;i<10;i++){float d=(i+1)*8;int sa=(int)(a*0.3f*(1-i*0.09f));using(var p=new Pen(Color.FromArgb(sa,160,140,80),0.6f)) g.DrawEllipse(p,cx-ev.DirX*d-3,cy-ev.DirY*d-3,6,6);}
        }
        static void PaintSpacePirate(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);cy+=(float)Math.Sin(t*40)*2;
            using(var b=new SolidBrush(Color.FromArgb(a,60,45,30))) g.FillEllipse(b,cx-10,cy-3,20,6);
            float sw=(float)Math.Sin(t*25)*3;PointF[] sail={new PointF(cx-2,cy-3),new PointF(cx+sw,cy-12),new PointF(cx+4,cy-3)};
            using(var b=new SolidBrush(Color.FromArgb(a*3/4,40,35,45))) g.FillPolygon(b,sail);
            using(var b=new SolidBrush(Color.FromArgb(a,200,200,200))) g.FillEllipse(b,cx+sw-1.5f,cy-13,3,3);
            float ex=cx-(ev.DirX>0?10:-10);using(var b=new SolidBrush(Color.FromArgb(a/3,255,100,30))) g.FillEllipse(b,ex-3,cy-2,6,4);
        }
        static void PaintCrystalDragon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(180*fade);
            for(int seg=0;seg<12;seg++){float d=seg*5,sx=cx-ev.DirX*d,sy=cy-ev.DirY*d+(float)Math.Sin(t*30+seg*0.8f)*3;
                float sz=4f-seg*0.25f;int sa=(int)(a*(1f-seg*0.06f));Color sc=seg%2==0?Color.FromArgb(sa,120,200,255):Color.FromArgb(sa,180,140,255);
                using(var b=new SolidBrush(sc)) g.FillEllipse(b,sx-sz,sy-sz*0.6f,sz*2,sz*1.2f);
                if(seg<6) using(var b=new SolidBrush(Color.FromArgb(sa/3,255,255,255))) g.FillEllipse(b,sx-1,sy-sz*0.4f,2,sz*0.4f);}
            float dir=ev.DirX>0?1:-1;
            using(var p=new Pen(Color.FromArgb(a,160,220,255),1f)){g.DrawLine(p,cx+dir*3,cy-2,cx+dir*6,cy-6);g.DrawLine(p,cx+dir*2,cy-1,cx+dir*5,cy-5);}
            for(int bp=0;bp<5;bp++){float bx=cx+dir*(8+bp*3),by=cy-1+(float)Math.Sin(t*50+bp)*2;int ba=(int)(a*0.4f*(1-bp*0.18f));
                using(var b=new SolidBrush(Color.FromArgb(ba,150,220,255))) g.FillEllipse(b,bx-1,by-1,2,2);}
        }
        static void PaintQuantumRift(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(160*fade);float pulse=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            var rng=new Random(ev.Seed+(int)(t*60));
            for(int line=0;line<8;line++){float ang=(float)(line*Math.PI/4+t*15),len=10+(float)rng.NextDouble()*20*pulse;
                float jx=(float)(rng.NextDouble()-0.5)*6,jy=(float)(rng.NextDouble()-0.5)*6;
                Color lc=line%2==0?Color.FromArgb(a,180,100,255):Color.FromArgb(a,100,200,255);
                float ex2=cx+(float)Math.Cos(ang)*len,ey2=cy+(float)Math.Sin(ang)*len;
                using(var p=new Pen(lc,0.8f)){g.DrawLine(p,cx+jx,cy+jy,(cx+ex2)/2+jx*2,(cy+ey2)/2+jy*2);g.DrawLine(p,(cx+ex2)/2+jx*2,(cy+ey2)/2+jy*2,ex2,ey2);}}
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.5f*pulse),200,150,255))) g.FillEllipse(b,cx-4,cy-4,8,8);
        }
        static void PaintCosmicDancer(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float spin=t*100;
            using(var b=new SolidBrush(Color.FromArgb(a,255,180,220))) g.FillEllipse(b,cx-2,cy-4,4,8);
            for(int limb=0;limb<4;limb++){float la=spin+limb*90,lr=6+(float)Math.Sin(t*45+limb)*2;
                float lx2=cx+(float)Math.Cos(la*Math.PI/180)*lr,ly2=cy+(float)Math.Sin(la*Math.PI/180)*lr;
                using(var p=new Pen(Color.FromArgb(a*3/4,255,200,230),0.8f)) g.DrawLine(p,cx,cy,lx2,ly2);
                using(var b=new SolidBrush(Color.FromArgb(a/2,255,255,200))) g.FillEllipse(b,lx2-1,ly2-1,2,2);}
            for(int r2=0;r2<8;r2++){float d=(r2+1)*5;int ra=(int)(a*0.3f*(1-r2*0.1f));
                using(var b=new SolidBrush(Color.FromArgb(ra,255,180,220))) g.FillEllipse(b,cx-ev.DirX*d-1.5f,cy-ev.DirY*d+(float)Math.Sin(t*50+r2)*4-0.5f,3,1);}
        }
        static void PaintPlasmaSnake(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(180*fade);
            for(int seg=0;seg<20;seg++){float d=seg*4,sx=cx-ev.DirX*d,sy=cy-ev.DirY*d+(float)Math.Sin(t*40+seg*0.6f)*(5+seg*0.2f);
                float sz=3f-seg*0.1f;int sa=(int)(a*(1f-seg*0.04f));int pulse=(int)(sa*(0.7f+0.3f*(float)Math.Sin(t*80+seg)));
                using(var b=new SolidBrush(Color.FromArgb(pulse,50,255,150))) g.FillEllipse(b,sx-sz,sy-sz*0.5f,sz*2,sz);
                if(seg%3==0&&seg<15){float jx=(float)Math.Sin(t*100+seg)*4,jy=(float)Math.Cos(t*90+seg)*3;
                    using(var p=new Pen(Color.FromArgb(sa/2,100,255,200),0.5f)) g.DrawLine(p,sx,sy,sx+jx,sy+jy);}}
            float dir=ev.DirX>0?1:-1;using(var b=new SolidBrush(Color.FromArgb(a,255,50,50))) g.FillEllipse(b,cx+dir*2,cy-1.5f,2,2);
        }
        static void PaintStarSurfer(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);
            using(var b=new SolidBrush(Color.FromArgb(a,80,60,40))) g.FillEllipse(b,cx-8,cy+1,16,3);
            using(var p=new Pen(Color.FromArgb(a/2,255,200,50),0.8f)) g.DrawEllipse(p,cx-8,cy+1,16,3);
            using(var b=new SolidBrush(Color.FromArgb(a,200,200,220))) g.FillEllipse(b,cx-2,cy-6,4,7);
            float lean=(float)Math.Sin(t*30)*3;
            using(var p=new Pen(Color.FromArgb(a*3/4,200,200,220),0.8f)){g.DrawLine(p,cx,cy-3,cx-5,cy-4+lean);g.DrawLine(p,cx,cy-3,cx+5,cy-4-lean);}
            for(int i=0;i<12;i++){float d=(i+1)*5;int sa=(int)(a*0.25f*(1-i*0.07f));
                using(var b=new SolidBrush(Color.FromArgb(sa,255,220,100))) g.FillEllipse(b,cx-ev.DirX*d-1,cy+3-ev.DirY*d+(float)Math.Sin(t*50+i)*2-1,2,2);}
        }
        static void PaintVoidMoth(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(150*fade);float wf=(float)Math.Sin(t*55)*8;
            for(int side=-1;side<=1;side+=2){float wx=cx+side*wf;
                using(var b=new SolidBrush(Color.FromArgb(a*2/3,30,20,50))) g.FillEllipse(b,wx-4+(side>0?0:-4),cy-5,8,10);
                for(int sp=0;sp<3;sp++){int spa=(int)(a*(0.5f+0.3f*(float)Math.Sin(t*60+sp+side)));
                    using(var b=new SolidBrush(Color.FromArgb(spa,100,50,200))) g.FillEllipse(b,wx+side*(1+sp)-1,cy-2+sp*2-1,2,2);}}
            using(var b=new SolidBrush(Color.FromArgb(a,50,30,70))) g.FillEllipse(b,cx-1.5f,cy-3,3,6);
            using(var p=new Pen(Color.FromArgb(a/2,120,80,180),0.5f)){g.DrawLine(p,cx,cy-3,cx-3,cy-7);g.DrawLine(p,cx,cy-3,cx+3,cy-7);}
        }
        static void PaintNeonJellyfish(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float pulse=(float)Math.Sin(t*35);
            float dr=6+pulse*1.5f;
            using(var b=new SolidBrush(Color.FromArgb(a*2/3,255,50,200))) g.FillEllipse(b,cx-dr,cy-dr*0.7f,dr*2,dr*1.2f);
            using(var b=new SolidBrush(Color.FromArgb(a/4,255,150,230))) g.FillEllipse(b,cx-dr*0.6f,cy-dr*0.4f,dr*1.2f,dr*0.6f);
            for(int ten=0;ten<7;ten++){float tx=cx-5+ten*1.7f;for(int seg=0;seg<8;seg++){
                float sy=cy+dr*0.5f+seg*2.5f+(float)Math.Sin(t*30+ten+seg*0.5f)*2,sx=tx+(float)Math.Sin(t*25+ten*0.7f+seg*0.3f)*1.5f;
                int sa=(int)(a*0.5f*(1-seg*0.1f));Color tc=ten%2==0?Color.FromArgb(sa,255,80,220):Color.FromArgb(sa,80,200,255);
                using(var b=new SolidBrush(tc)) g.FillEllipse(b,sx-0.8f,sy-0.5f,1.6f,1f);}}
        }
        static void PaintGalaxySpiral(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(140*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;float spin=t*60;
            for(int arm=0;arm<3;arm++){float armOff=arm*120;for(int pt=0;pt<30;pt++){
                float ang=(float)((spin+armOff+pt*12)*Math.PI/180),r2=(pt*0.8f+2)*op;
                float px=cx+(float)Math.Cos(ang)*r2,py=cy+(float)Math.Sin(ang)*r2*0.5f;int pa=(int)(a*0.5f*(1-pt*0.025f));
                Color pc=arm==0?Color.FromArgb(pa,150,180,255):arm==1?Color.FromArgb(pa,255,180,150):Color.FromArgb(pa,180,150,255);
                using(var b=new SolidBrush(pc)) g.FillEllipse(b,px-1,py-1,2,2);}}
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.4f*op),255,250,220))) g.FillEllipse(b,cx-3,cy-2,6,4);
        }
        static void PaintMagicCarpet(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float wave=(float)Math.Sin(t*30)*2;
            PointF[] carpet={new PointF(cx-10,cy+wave),new PointF(cx-6,cy-2-wave),new PointF(cx+6,cy-2+wave),new PointF(cx+10,cy-wave)};
            using(var b=new SolidBrush(Color.FromArgb(a*3/4,120,30,30))) g.FillPolygon(b,carpet);
            using(var b=new SolidBrush(Color.FromArgb(a/2,200,160,50))) g.FillEllipse(b,cx-3,cy-2,6,2);
            for(int i=0;i<8;i++){float d=(i+1)*6;int sa=(int)(a*0.3f*(1-i*0.1f));
                using(var b=new SolidBrush(Color.FromArgb(sa,255,200,80))) g.FillEllipse(b,cx-ev.DirX*d-1,cy-ev.DirY*d+(float)Math.Sin(t*40+i)*3-1,2,2);}
        }
        static void PaintSpaceLantern(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(160*fade);float fl=(float)(0.7+0.3*Math.Sin(t*70));
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.7f*fl),255,180,80))) g.FillEllipse(b,cx-4,cy-5,8,10);
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.4f*fl),255,240,180))) g.FillEllipse(b,cx-2,cy-2,4,4);
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.1f*fl),255,200,100))) g.FillEllipse(b,cx-8,cy-8,16,16);
        }
        static void PaintCosmicOwl(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);float wf=(float)Math.Sin(t*40)*6;
            using(var b=new SolidBrush(Color.FromArgb(a,80,65,50))) g.FillEllipse(b,cx-4,cy-3,8,8);
            for(int side=-1;side<=1;side+=2) using(var b=new SolidBrush(Color.FromArgb(a*3/4,90,75,55))) g.FillEllipse(b,cx+side*4+side*Math.Abs(wf)*0.5f,cy-1+wf*side*0.3f,6,4);
            for(int side=-1;side<=1;side+=2){using(var b=new SolidBrush(Color.FromArgb(a,255,200,50))) g.FillEllipse(b,cx+side*2-1.5f,cy-2,3,3);
                using(var b=new SolidBrush(Color.FromArgb(a,30,20,10))) g.FillEllipse(b,cx+side*2-0.5f,cy-1,1.2f,1.2f);}
            for(int i=0;i<6;i++){float d=(i+1)*6;int sa=(int)(a*0.2f*(1-i*0.12f));
                using(var b=new SolidBrush(Color.FromArgb(sa,255,220,100))) g.FillEllipse(b,cx-ev.DirX*d-1,cy-ev.DirY*d-1,2,2);}
        }
        static void PaintWarpDrive(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(180*fade);float it=t<0.2f?t/0.2f:t>0.7f?(1-t)/0.3f:1f;
            var rng=new Random(ev.Seed);for(int line=0;line<20;line++){float ang=(float)(rng.NextDouble()*Math.PI*2);
                float r1=(8+(float)rng.NextDouble()*5)*it,r2=(r1+15+(float)rng.NextDouble()*20)*it;
                int la=(int)(a*0.5f*(0.5f+(float)Math.Sin(t*60+line)*0.5f));
                using(var p=new Pen(Color.FromArgb(la,150,200,255),0.7f)) g.DrawLine(p,cx+(float)Math.Cos(ang)*r1,cy+(float)Math.Sin(ang)*r1*0.5f,cx+(float)Math.Cos(ang)*r2,cy+(float)Math.Sin(ang)*r2*0.5f);}
            float fr=4*it;using(var b=new SolidBrush(Color.FromArgb((int)(a*0.6f*it),200,230,255))) g.FillEllipse(b,cx-fr,cy-fr*0.5f,fr*2,fr);
        }
        static void PaintSpaceKoi(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(170*fade);cy+=(float)Math.Sin(t*35)*3;
            using(var b=new SolidBrush(Color.FromArgb(a,255,120,50))) g.FillEllipse(b,cx-6,cy-3,12,6);
            using(var b=new SolidBrush(Color.FromArgb(a*3/4,255,240,230))){g.FillEllipse(b,cx-2,cy-2,5,3);g.FillEllipse(b,cx+2,cy,3,2);}
            float dir=ev.DirX>0?-1:1;float tw=(float)Math.Sin(t*45)*4;
            PointF[] tail={new PointF(cx+dir*6,cy),new PointF(cx+dir*10,cy-3+tw),new PointF(cx+dir*10,cy+3+tw)};
            using(var b=new SolidBrush(Color.FromArgb(a*2/3,255,100,40))) g.FillPolygon(b,tail);
            using(var b=new SolidBrush(Color.FromArgb(a,30,30,30))) g.FillEllipse(b,cx-dir*3.5f,cy-1,1.5f,1.5f);
            for(int i=0;i<5;i++){float bx=cx+dir*(8+i*4),by=cy-2+(float)Math.Sin(t*40+i)*3;int ba=(int)(a*0.2f*(1-i*0.15f));
                using(var p=new Pen(Color.FromArgb(ba,200,220,255),0.5f)) g.DrawEllipse(p,bx-1.5f,by-1.5f,3,3);}
        }
        static void PaintCelestialHarp(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(150*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            using(var p=new Pen(Color.FromArgb(a,200,180,100),1.2f)){g.DrawArc(p,cx-12,cy-15,24,30,200,140);g.DrawLine(p,cx-8,cy+10,cx+8,cy-12);}
            for(int s=0;s<7;s++){float sx=cx-6+s*2,vib=(float)Math.Sin(t*80+s*3)*(1+s*0.3f)*op;
                int sa=(int)(a*0.6f*(0.5f+0.5f*(float)Math.Sin(t*60+s*2)));Color sc=s%2==0?Color.FromArgb(sa,255,230,150):Color.FromArgb(sa,200,180,255);
                using(var p=new Pen(sc,0.5f)) g.DrawBezier(p,sx,cy+8,sx+vib*0.5f,cy+2,sx+vib,cy-4,sx+s*0.5f,cy-10);}
            for(int n=0;n<5;n++){float ny=cy-10-n*5-t*30*op,nx=cx+(float)Math.Sin(t*20+n*2)*8;int na=(int)(a*0.4f*(1-n*0.15f));
                if(na>0) using(var b=new SolidBrush(Color.FromArgb(na,255,240,180))) g.FillEllipse(b,nx-1,ny-1,2,2);}
        }
        static void PaintMeteorDragon(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w+ev.DirX*t*w*1.4f,cy=ev.Y*h+ev.DirY*t*h*1.4f;int a=(int)(180*fade);
            for(int seg=0;seg<15;seg++){float d=seg*4,sx=cx-ev.DirX*d,sy=cy-ev.DirY*d+(float)Math.Sin(t*35+seg*0.7f)*3;
                float sz=3.5f-seg*0.15f;int sa=(int)(a*(1f-seg*0.05f));
                Color sc=Color.FromArgb(sa,255,(int)(120-seg*5),(int)(30+seg*3));
                using(var b=new SolidBrush(sc)) g.FillEllipse(b,sx-sz,sy-sz*0.5f,sz*2,sz);
                if(seg%2==0) using(var b=new SolidBrush(Color.FromArgb(sa/3,255,200,50))) g.FillEllipse(b,sx-1,sy-sz*0.3f,2,sz*0.3f);}
            float dir=ev.DirX>0?1:-1;
            using(var p=new Pen(Color.FromArgb(a,255,200,50),1f)){g.DrawLine(p,cx+dir*3,cy-2,cx+dir*7,cy-5);g.DrawLine(p,cx+dir*2,cy-1,cx+dir*6,cy-4);}
            // Fire breath
            for(int f=0;f<6;f++){float fx=cx+dir*(5+f*3),fy=cy-1+(float)Math.Sin(t*70+f)*2;int fa=(int)(a*0.5f*(1-f*0.12f));
                using(var b=new SolidBrush(Color.FromArgb(fa,255,150,30))) g.FillEllipse(b,fx-1.5f,fy-1,3,2);}
        }
        static void PaintNorthernLights(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(120*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            Color[] aur={Color.FromArgb(50,255,100),Color.FromArgb(80,200,255),Color.FromArgb(150,50,255),Color.FromArgb(255,100,200)};
            for(int band=0;band<4;band++){for(int pt=0;pt<20;pt++){
                float px=cx-40+pt*4+(float)Math.Sin(t*15+band*2+pt*0.3f)*5;
                float py=cy-15+band*10+(float)Math.Sin(t*20+pt*0.5f+band)*6;
                float stretch=8+(float)Math.Sin(t*25+pt+band)*3;int pa=(int)(a*0.4f*op*(0.5f+0.5f*(float)Math.Sin(t*30+pt+band)));
                using(var b=new SolidBrush(Color.FromArgb(pa,aur[band].R,aur[band].G,aur[band].B))) g.FillEllipse(b,px-2,py-stretch*0.5f,4,stretch);}}
        }
        static void PaintDarkMatter(Graphics g, int w, int h, CelestialEvents.CelestialEvent ev, float t, float fade) {
            float cx=ev.X*w,cy=ev.Y*h;int a=(int)(140*fade);float op=t<0.15f?t/0.15f:t>0.85f?(1-t)/0.15f:1f;
            // Invisible mass — seen only by gravitational lensing
            float r=12*op;
            for(int ring=0;ring<4;ring++){float rr=r+ring*5;int ra=(int)(a*0.2f*(1-ring*0.04f));
                using(var p=new Pen(Color.FromArgb(ra,80,60,120),0.8f)) g.DrawEllipse(p,cx-rr,cy-rr,rr*2,rr*2);}
            // Dark core
            using(var b=new SolidBrush(Color.FromArgb((int)(a*0.3f*op),20,10,40))) g.FillEllipse(b,cx-r*0.6f,cy-r*0.6f,r*1.2f,r*1.2f);
            // Bent starlight particles orbiting
            var rng=new Random(ev.Seed);
            for(int pt=0;pt<12;pt++){float ang=(float)(rng.NextDouble()*Math.PI*2)+t*20;float pr=r+2+(float)rng.NextDouble()*8;
                float px=cx+(float)Math.Cos(ang)*pr,py=cy+(float)Math.Sin(ang)*pr;
                int pa=(int)(a*0.5f*(0.5f+0.5f*(float)Math.Sin(t*50+pt)));
                using(var b=new SolidBrush(Color.FromArgb(pa,160,140,200))) g.FillEllipse(b,px-1,py-1,2,2);}
        }
    }
}
