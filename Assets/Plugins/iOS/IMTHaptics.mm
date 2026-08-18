#import <UIKit/UIKit.h>

// Üretici pahalı: her dokunuşta yenisini kurmak Taptic motorunu soğuk yakalar ve ilk vuruş gecikir.
// Üçü de tutulur, kullanıldıktan sonra prepare ile sıcak bırakılır.
static UIImpactFeedbackGenerator *gLight  = nil;
static UIImpactFeedbackGenerator *gMedium = nil;
static UIImpactFeedbackGenerator *gHeavy  = nil;

static UIImpactFeedbackGenerator *GeneratorFor(int style)
{
    switch (style) {
        case 0:
            if (gLight == nil)
                gLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            return gLight;
        case 2:
            if (gHeavy == nil)
                gHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
            return gHeavy;
        default:
            if (gMedium == nil)
                gMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            return gMedium;
    }
}

extern "C" void _IMTHapticPrepare(int style)
{
    dispatch_async(dispatch_get_main_queue(), ^{ [GeneratorFor(style) prepare]; });
}

extern "C" void _IMTHapticImpact(int style)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UIImpactFeedbackGenerator *g = GeneratorFor(style);
        [g impactOccurred];
        [g prepare];
    });
}

// Bir ada yeniden şekillendi: orta, sonra sert. Android'deki dalga biçiminin karşılığı.
extern "C" void _IMTHapticDouble(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        [GeneratorFor(1) impactOccurred];
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.071 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{
            UIImpactFeedbackGenerator *heavy = GeneratorFor(2);
            [heavy impactOccurred];
            [heavy prepare];
        });
    });
}
