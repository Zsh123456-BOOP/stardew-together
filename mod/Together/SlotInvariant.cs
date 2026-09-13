namespace Together;
public static class SlotInvariant {
    public static int Check(int index,int count) => index>=0&&index<count?index:throw new InvalidOperationException($"invalid_selected_slot:index={index}:count={count}");
}
